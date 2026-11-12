# FRMS PRN212 — WPF ↔ REST API connection test

This is the first minimal WPF/MVVM milestone: a Windows desktop client calls the existing FRMS backend health endpoint. It does **not** connect to PostgreSQL directly or change the backend.

## Requirements

- Windows 10/11; Visual Studio with **.NET desktop development** workload, supporting .NET 10, or .NET 10 SDK.
- Running FRMS backend at `https://localhost:7235` (the user-provided local address).
- PostgreSQL correctly configured behind FRMS.

## Run

1. Keep FRMS API running in the first terminal.
2. On Windows, trust the local HTTPS development certificate once if required:

   ```powershell
   dotnet dev-certs https --trust
   ```

3. From this directory:

   ```powershell
   dotnet restore
   dotnet run
   ```

   Alternatively open `FRMS.PRN212.WpfConnection.csproj` in Visual Studio and click Start.
4. Click **Test API / Database Connection**.

## Expected

- `Connected` and `HTTP 200` means the `/health` endpoint succeeded. FRMS documents that `/health` checks database connectivity; this does **not** prove migrations or business flows are correct.
- `Connection failed`: check API address, whether backend is running, Windows firewall and dev HTTPS certificate.
- `Unhealthy`: inspect backend logs and PostgreSQL settings.
- `Timeout`: inspect backend availability or a blocked connection.

## Customize

- Change `client.BaseAddress` in `App.xaml.cs` when the backend address changes.
- For actual FRMS operations, use API paths like `api/v1/auth/customer/login` and add typed request/response DTOs; do not bypass backend authorization.
- Never disable TLS certificate validation or embed database secrets in WPF.

## Status

Source bundle created; not compiled here because .NET SDK/Windows WPF runtime is not installed in the generation environment. Build and run on the user's Windows machine.
