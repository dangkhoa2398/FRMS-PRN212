# FRMS REST API integration for PRN212 WPF

This folder is a **non-invasive integration component**, not a replacement WPF application. The repository's `main` branch currently does not expose a WPF `.csproj` / `App.xaml` through GitHub, so the original UI and ViewModels have deliberately **not** been overwritten.

## Connection

- HTTP (development default): `http://localhost:5164/`
- HTTPS: `https://localhost:7235/` (requires trusting the .NET local development certificate)
- Health check: `GET /health`
- Public facility list: `GET /api/v1/facilities?page=1&pageSize=20`
- Protected requests: call `SetBearerToken(jwt)` after logging in; clear it on logout.
- Database: only the ASP.NET backend communicates with PostgreSQL.

## Use in the existing WPF project

Place `FrmsApiClient.cs` inside the folder containing the original WPF `.csproj`, e.g. `YourWpfProject/Services/FrmsApiClient.cs`. SDK-style .NET projects normally auto-include `**/*.cs` in their own project directory.

Use from a ViewModel / application service (not from XAML):

```csharp
using Frms.Wpf.Integration;

using var api = new FrmsApiClient("http://localhost:5164/");
bool healthy = await api.CheckHealthAsync();

using var facilities = await api.GetFacilitiesAsync();
string json = facilities.RootElement.GetRawText();
```

Use an application-scoped `FrmsApiClient` instead of creating a new instance for every button click. If the existing WPF project already has `ApiService`, `HttpClient` or dependency injection, merge these methods into that service rather than registering a duplicate.

The WPF project source must be pushed to GitHub before the integration can be wired to the concrete existing ViewModels and built/tested. No live localhost call or WPF build was performed here.
