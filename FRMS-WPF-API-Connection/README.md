# FRMS WPF ↔ ASP.NET Core API (PRN212)

A minimal, standalone WPF MVVM connectivity test project using only .NET BCL.

## Requirements
- Windows 10/11, Visual Studio 2022+ with .NET desktop development workload
- .NET 10 SDK
- Existing FRMS API running locally

## Run
1. Start the FRMS backend with the existing HTTPS profile. The backend advertises `http://localhost:5164` and `https://localhost:7235`.
2. Open `FRMS.Wpf.csproj` in Visual Studio and run (F5), or from PowerShell: `dotnet run --project FRMS.Wpf.csproj`.
3. Keep the default API base URL `http://localhost:5164/`.
4. Press **Kiểm tra /health**. A successful response is HTTP 200; it means the backend health check passes, not that all business features/schema are verified.
5. Press **GET /api/v1/facilities** to request the public facilities listing. Even an empty `data` array can indicate success.

## HTTPS
Change the base URL to `https://localhost:7235/` and run `dotnet dev-certs https --trust` on the Windows development machine if the developer certificate is not trusted. **Do not bypass TLS certificate validation in code.**

## Integrate into an existing WPF solution
Copy `Services/FrmsApiClient.cs` and `Commands/AsyncRelayCommand.cs` to your WPF project; adjust namespaces. Do not overwrite existing `App.xaml` or `MainWindow.xaml` blindly. Use a `HttpClient` instance/reusable service for all REST calls, and add Bearer JWT only for protected routes. The backend remains the only component connecting to PostgreSQL.

## Verified from backend main
- `backend/Frms.Api/Properties/launchSettings.json`: HTTP 5164, HTTPS 7235.
- `backend/Frms.Api/Program.cs`: anonymous `/health`.
- `docs/FRMS_API_V11.md`: public GET `/api/v1/facilities` and JSON response envelopes.

This sample is a connectivity proof and does not implement login, role authorization, booking or ticket workflows.
