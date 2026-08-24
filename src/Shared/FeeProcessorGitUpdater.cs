using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using CliWrap;

namespace FeeSyncer.Shared;

public sealed record FeeProcessorGitRequest(
    string AppPath,
    string Repository,
    string Branch,
    string Tag,
    string SshUsername = "git",
    string SshKeyPath = "",
    string SshPassphrase = "",
    string GitPath = "git");

public sealed class FeeProcessorGitUpdater
{
    public string Update(FeeProcessorGitRequest request, Action<string>? progress = null)
    {
        EnsureCheckout(request, progress);

        using var repository = new Repository(request.AppPath);
        var localChanges = repository.RetrieveStatus(new StatusOptions
        {
            IncludeIgnored = true,
            IncludeUntracked = true,
            RecurseIgnoredDirs = true,
            RecurseUntrackedDirs = true
        })
            .Where(status => status.State != FileStatus.Ignored)
            .Select(status => $"{status.State}: {status.FilePath}")
            .Take(50)
            .ToList();
        if (localChanges.Count > 0)
        {
            progress?.Invoke("Fee Processor checkout contains local changes:");
            foreach (var change in localChanges) progress?.Invoke($"  {change}");
            throw new InvalidOperationException(
                $"Fee Processor checkout has {localChanges.Count} local change(s). Commit or remove them before updating.");
        }

        var origin = repository.Network.Remotes["origin"]
            ?? throw new InvalidOperationException("The Fee Processor checkout has no origin remote.");
        progress?.Invoke($"Git origin: {origin.Url}");
        progress?.Invoke(origin.Url.StartsWith("git@", StringComparison.OrdinalIgnoreCase) || origin.Url.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)
            ? "Git authentication mode: SSH agent/Pageant"
            : "Git authentication mode: HTTPS/default credential provider");
        var normalizedOrigin = NormalizeRepositoryUrl(origin.Url);
        if (!string.Equals(origin.Url, normalizedOrigin, StringComparison.Ordinal))
        {
            repository.Network.Remotes.Update(origin.Name, remote => remote.Url = normalizedOrigin);
            origin = repository.Network.Remotes[origin.Name]
                ?? throw new InvalidOperationException("The Fee Processor origin remote could not be updated.");
        }
        if (!string.Equals(NormalizeRepositoryUrl(origin.Url), NormalizeRepositoryUrl(request.Repository), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Fee Processor origin remote does not match the configured repository.");

        progress?.Invoke("Fetching branches and tags...");
        try
        {
            if (IsSshRepository(request.Repository))
            {
                progress?.Invoke("Using system Git SSH transport...");
                RunExternalGit(request, ["fetch", "--prune", origin.Name], progress);
            }
            else
            {
                Commands.Fetch(repository, origin.Name, origin.FetchRefSpecs.Select(spec => spec.Specification),
                    new FetchOptions { Prune = true, CredentialsProvider = Credentials(request) }, $"Fetch {origin.Name}");
            }
        }
        catch (Exception exception)
        {
            progress?.Invoke($"Git fetch failed: {exception.GetType().Name}: {exception.Message}");
            if (exception.InnerException is not null)
                progress?.Invoke($"Git inner error: {exception.InnerException.Message}");
            throw;
        }

        string commit;
        if (!string.IsNullOrWhiteSpace(request.Tag))
        {
            var tagName = request.Tag == "*" ? FindLatestTag(repository) : request.Tag;
            var tag = repository.Tags[tagName]
                ?? throw new InvalidOperationException($"Tag '{tagName}' was not found.");
            Commands.Checkout(repository, GetTagCommit(tag));
            commit = repository.Head.Tip.Sha;
            progress?.Invoke($"Checked out tag {tagName}.");
        }
        else
        {
            var branchName = string.IsNullOrWhiteSpace(request.Branch) ? "main" : request.Branch;
            var remoteBranch = repository.Branches[$"origin/{branchName}"]
                ?? throw new InvalidOperationException($"Remote branch '{branchName}' was not found.");
            var localBranch = repository.Branches[branchName] ?? repository.CreateBranch(branchName, remoteBranch.Tip);
            Commands.Checkout(repository, localBranch);
            repository.Reset(ResetMode.Hard, remoteBranch.Tip);
            commit = remoteBranch.Tip.Sha;
            progress?.Invoke($"Checked out branch {branchName}.");
        }

        return commit;
    }

    private static void EnsureCheckout(FeeProcessorGitRequest request, Action<string>? progress)
    {
        if (Repository.IsValid(request.AppPath)) return;

        if (Directory.Exists(request.AppPath) && Directory.EnumerateFileSystemEntries(request.AppPath).Any())
            throw new InvalidOperationException($"Fee Processor directory is not a Git checkout and is not empty: {request.AppPath}");

        Directory.CreateDirectory(request.AppPath);
        progress?.Invoke($"Cloning Fee Processor repository into {request.AppPath}...");
        if (IsSshRepository(request.Repository))
        {
            progress?.Invoke("Using system Git SSH transport...");
            RunExternalGit(request, ["clone", request.Repository, request.AppPath], progress, Directory.GetParent(request.AppPath)?.FullName);
        }
        else
        {
            var options = new CloneOptions
            {
                IsBare = false,
                Checkout = true,
            };
            options.FetchOptions.CredentialsProvider = Credentials(request);
            Repository.Clone(request.Repository, request.AppPath, options);
        }
    }

    private static CredentialsHandler? Credentials(FeeProcessorGitRequest request)
    {
        return null;
    }

    private static bool IsSshRepository(string repository) =>
        repository.StartsWith("git@", StringComparison.OrdinalIgnoreCase)
        || repository.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase);

    private static void RunExternalGit(FeeProcessorGitRequest request, IReadOnlyList<string> arguments,
        Action<string>? progress = null, string? workingDirectory = null)
    {
        var command = Cli.Wrap(string.IsNullOrWhiteSpace(request.GitPath) ? "git" : request.GitPath)
            .WithArguments(arguments)
            .WithWorkingDirectory(workingDirectory ?? request.AppPath)
            .WithStandardOutputPipe(PipeTarget.ToDelegate(line => progress?.Invoke(line)))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(line => progress?.Invoke(line)))
            .WithValidation(CommandResultValidation.None);

        if (!string.IsNullOrWhiteSpace(request.SshKeyPath) && File.Exists(request.SshKeyPath))
        {
            var sshCommand = $"ssh -i \"{request.SshKeyPath}\" -o IdentitiesOnly=yes";
            command = command.WithEnvironmentVariables(new Dictionary<string, string?>
            {
                ["GIT_SSH_COMMAND"] = sshCommand
            });
        }

        CommandResult result;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            result = command
                .ExecuteAsync(timeout.Token)
                .GetAwaiter()
                .GetResult();
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException("Git SSH operation timed out after 120 seconds.");
        }
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Git SSH operation failed with exit code {result.ExitCode}.");
    }

    private static string NormalizeRepositoryUrl(string repository)
    {
        if (!repository.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
            return repository;

        var separator = repository.IndexOf(':');
        return separator > 0
            ? $"ssh://{repository[..separator]}/{repository[(separator + 1)..]}"
            : repository;
    }

    private static string FindLatestTag(Repository repository)
    {
        var tag = repository.Tags
            .Select(candidate => new { candidate, Commit = GetTagCommit(candidate) })
            .OrderByDescending(candidate => candidate.Commit.Committer.When)
            .FirstOrDefault();
        return tag?.candidate.FriendlyName ?? throw new InvalidOperationException("No remote tags were found.");
    }

    private static Commit GetTagCommit(Tag tag) => tag.Target switch
    {
        Commit commit => commit,
        TagAnnotation annotation when annotation.Target is Commit commit => commit,
        _ => throw new InvalidOperationException($"Tag '{tag.FriendlyName}' does not point to a commit."),
    };
}
