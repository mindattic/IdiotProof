# IdiotProof.Brokers.Ibkr (Dormant)

This project is **not part of the active build**. It is preserved on disk so
that Interactive Brokers support can be plugged back in without re-implementing
the wrapper from scratch.

## Status

The platform is currently Alpaca-only. The active broker abstraction lives in
`IdiotProof.Brokers/IBrokerClient.cs`, with `AlpacaBrokerClient` and
`SandboxBrokerClient` as the only registered implementations.

## How to re-enable

1. Add `IdiotProof.Brokers.Ibkr.csproj` to `IdiotProof.sln` and
   `IdiotProof.slnx`.
2. Add a `ProjectReference` from `IdiotProof.Engine` to this project.
3. Re-add the `BrokerType.Ibkr` enum value to `IdiotProof.Models/Enums.cs`.
4. Re-introduce IBKR settings (host/port/clientId/usePaper) to
   `IdiotProof.Engine/Settings/AppSettings.cs`.
5. Register `IbkrBrokerClient` in `IdiotProof.Engine/ServiceRegistration.cs`
   alongside the existing Alpaca registration.
