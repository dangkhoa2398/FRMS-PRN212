# FRMS WPF interface system

PRN212 uses **Light** and **Dark** palettes with a shared style library. UI design follows practical navigation, form and card patterns summarized by [GeeksforGeeks – UI Design Patterns](https://www.geeksforgeeks.org/system-design/user-interface-ui-design-patterns/).

- **Navigation pattern:** the sidebar maintains Customer/Staff/Manager routes and shows the selected page with a shared route converter. It keeps existing `ICommand` bindings.
- **Form pattern:** the account type and field labels remain explicit; focus borders, consistent input heights, command feedback and validation messages are preserved.
- **Card pattern:** contract, reservation, storage, ticket, visit and employee records use a shared surface/border/elevation hierarchy.
- **Accessibility:** avoid hiding state only in color; status names remain visible. No new destructive operations or modal prompts were introduced.
- **Dark mode** refers to appearance; it is *not* a deceptive "dark pattern".

## Theme resources

| File | Purpose |
| --- | --- |
| `Themes/Light.xaml` | Light semantic brush palette |
| `Themes/Dark.xaml` | Dark semantic brush palette |
| `Themes/Controls.xaml` | Shared Button, TextBox, PasswordBox, ComboBox, DatePicker and card styles |
| `Theming/ThemeManager.cs` | Swaps merged palettes and remembers the last selection |
| `Theming/RouteMatchConverter.cs` | Active menu selection for Customer and employee routes |

Views should use `{DynamicResource PageBackgroundBrush}`, `{DynamicResource SurfaceBrush}`, `{DynamicResource TextPrimaryBrush}` and other semantic resources rather than hard-coded colors. The Light/Dark button is available on Login and in both role shells. The selected appearance is stored in the user's ApplicationData folder as `FRMS-PRN212/theme.txt` and does not store login/session data.

## Verification

The Windows WPF tests validate that both palettes expose the same keys and that active navigation route matching works. The CI WPF build validates the compiled shared styles and view XAML. These checks do not replace manual visual review at different Windows scaling factors, screen sizes and contrast settings.
