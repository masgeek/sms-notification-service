using LibGit2Sharp;
using LibGit2Sharp.Handlers;

namespace FeeSyncer.Shared;

public sealed record FeeProcessorGitRequest(
    string AppPath,
    string Repository,
    string Branch,
    string Tag,
    string SshUsername = "git",
    string SshKeyPath = "",
    string SshPassphrase = "");

public sealed class FeeProcessorGitUpdater
{
    public string Update(FeeProcessorGitRequest request, Action<string>? progress = null)
    {
        EnsureCheckout(request, progress);

        using var repository = new Repository(request.AppPath);
        if (repository.RetrieveStatus().Any())
            throw new InvalidOperationException("Fee Processor checkout has local changes.");

        var origin = repository.Network.Remotes["origin"]
            ?? throw new InvalidOperationException("The Fee Processor checkout has no origin remote.");
        progress?.Invoke($"Git origin: {origin.Url}");
        progress?.Invoke(origin.Url.StartsWith("git@", StringComparison.OrdinalIgnoreCase) || origin.Url.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)
            ? "Git authentication mode: SSH agent/Pageant"
            : "Git authentication mode: HTTPS/default credential provider");
        if (!string.Equals(origin.Url, request.Repository, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Fee Processor origin remote does not match the configured repository.");

        progress?.Invoke("Fetching branches and tags...");
        try
        {
            Commands.Fetch(repository, origin.Name, origin.FetchRefSpecs.Select(spec => spec.Specification),
                new FetchOptions { Prune = true, CredentialsProvider = Credentials(request) }, $"Fetch {origin.Name}");
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
        Repository.Clone(request.Repository, request.AppPath, new CloneOptions
        {
            IsBare = false,
            Checkout = true,
            FetchOptions = new FetchOptions { CredentialsProvider = Credentials(request) },
        });
    }

    private static CredentialsHandler? Credentials(FeeProcessorGitRequest request)
    {
        if (!request.Repository.StartsWith("git@", StringComparison.OrdinalIgnoreCase)
            && !request.Repository.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
            return null;
        // LibGit2Sharp delegates SSH key handling to the configured SSH agent/Pageant.
        // The key path remains available for diagnostics and external Git setups.
        return (_, _, _) => new DefaultCredentials();
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
