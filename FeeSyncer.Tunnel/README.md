# FeeSyncer Tunnel

Tunnel components for exposing school-side FeeProcessor installations.

Each school server runs:

- `D:\Dev\php\fee-processor-new` on `127.0.0.1:8001`.
- The existing C# school agent.
- `FeeSyncer.Tunnel.Client` as a Windows service.

The relay maps school hostnames to outbound tunnel connections:

```text
kambui.munywele.co.ke
    -> public relay
    -> Kambui tunnel client
    -> http://127.0.0.1:8001
```

Projects:

- `Protocol`: shared versioned tunnel contracts.
- `Relay`: public relay and registration skeleton.
- `Client`: school-side Windows service skeleton.

The first slice implements protocol contracts and relay registration/heartbeat.
HTTP request streaming and persistent control-plane storage are next.
