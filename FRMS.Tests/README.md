# PRN212 testing

## Automated tests (no backend required)

- **FRMS.Tests** (`net10.0`, Linux): HTTP/JSON, authentication/session handling, all major API verbs/routes, error/status/cancellation, health checks, customer ViewModel filters and record states.
- **FRMS.WpfTests** (`net10.0-windows`, Windows STA): customer login/navigation, reservation/storage/ticket/visit commands and failures; Staff/Manager routes, work-item history/date filters, check-in/out, ticket assignment/completion, storage status, handover and inspection commands.
- **WPF build** (`net10.0-windows`, Windows): compiles the application and XAML.

On Windows with .NET SDK 10:

```powershell
dotnet test FRMS.Tests/FRMS.Tests.csproj --configuration Release
dotnet test FRMS.WpfTests/FRMS.WpfTests.csproj --configuration Release
dotnet build FRMS/FRMS.csproj --configuration Release
```

The GitHub Actions workflow runs all three gates on pushes and pull requests. HTTP traffic in these unit and ViewModel tests is simulated: **passing does not prove the live backend, PostgreSQL, permissions, stored procedures or actual UI interactions work**.

## Live FRMS smoke tests (opt-in)

Start FRMS API and PostgreSQL locally. Run the read-only CLI from the PRN212 repository root:

```powershell
$env:PRN212_API_BASE_URL = "http://localhost:5164/"
dotnet run --project FRMS.SmokeTests/FRMS.SmokeTests.csproj --configuration Release
```

This verifies health, public facility catalog and rejection of unauthenticated profile requests. To exercise real role-specific endpoints, also supply test accounts through environment variables **locally**, never commit credentials:

```powershell
$env:PRN212_CUSTOMER_PHONE = "<test-customer-phone>"
$env:PRN212_CUSTOMER_PASSWORD = "<test-customer-password>"
$env:PRN212_STAFF_EMAIL = "<test-staff-email>"
$env:PRN212_STAFF_PASSWORD = "<test-staff-password>"
$env:PRN212_MANAGER_EMAIL = "<test-manager-email>"
$env:PRN212_MANAGER_PASSWORD = "<test-manager-password>"
dotnet run --project FRMS.SmokeTests/FRMS.SmokeTests.csproj --configuration Release
```

The smoke tool prints PASS/FAIL/SKIP counts and exits non-zero on failures. Missing role credentials are explicitly reported as SKIP, not PASS. No business mutations are performed. HTTPS development certificates must be trusted normally; do not disable TLS validation.

## Manual end-to-end scenarios still required

Use a disposable, seeded **test database**, with separate Customer, Staff and Manager accounts:

1. Customer: sign in/out; choose a facility and unit type; check all combinations of size/mode/month filters; create reservation; view contracts/invoices; schedule an ACCESS visit; create a support ticket; inspect empty/error states.
2. Staff: review today/history work items; filter by date/status; check in/out an ACCESS visit; claim and complete inspection; confirm return; complete a ticket; verify forbidden actions are blocked.
3. Manager: list reservations and visits; create/update storage units; assign a same-facility Staff member; assign a physical unit at handover; finalize handover. Verify cross-facility operations are rejected.
4. Exercise payment, concurrency, repeated requests, server-side authorization, database invariants, full check-in/check-out and UI layout manually or through dedicated backend E2E suites.

**Coverage boundary:** automated PRN212 checks cover the desktop client and request contracts. Real PostgreSQL transactions, real permissions and full UI automation require a configured backend/test environment and are not asserted by the mock-based suites.
