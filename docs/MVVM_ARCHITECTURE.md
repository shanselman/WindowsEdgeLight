# MVVM Architecture Guide

## What is MVVM?

**MVVM (Model-View-ViewModel)** is a software architectural pattern that separates an application's user interface (View) from its business logic (Model) through an intermediary layer called the ViewModel. This pattern is particularly well-suited for WPF applications and provides several key benefits:

### Core Components

1. **Model**: Represents the data and business logic of the application
2. **View**: The user interface (XAML + minimal code-behind)
3. **ViewModel**: Acts as a bridge between View and Model, exposing data and commands for binding

### MVVM Flow

```
┌──────────┐         ┌──────────────┐         ┌─────────┐
│          │ Binding │              │ Updates │         │
│   View   │◄────────┤  ViewModel   │────────►│  Model  │
│  (XAML)  │         │              │         │         │
│          │ Events  │  (Commands,  │ Queries │         │
│          │────────►│  Properties) │◄────────┤         │
└──────────┘         └──────────────┘         └─────────┘
```

## Why MVVM for Windows Edge Light?

Before refactoring, `MainWindow.xaml.cs` was a 1,224-line monolithic class containing:
- UI event handlers
- Business logic (brightness, color temperature)
- Native Windows API calls (P/Invoke)
- Service management (hotkeys, mouse hooks, system tray)
- Monitor management
- Window positioning logic

**Problems with the original approach:**
1. **Tight coupling**: UI and business logic were intertwined
2. **Low testability**: Couldn't unit test business logic without UI
3. **Poor maintainability**: Single file responsible for too many concerns
4. **Limited reusability**: Logic couldn't be shared between windows

**Benefits of MVVM refactoring:**
1. **Separation of Concerns**: Each component has a single, well-defined responsibility
2. **Testability**: ViewModels and Services can be unit tested independently
3. **Maintainability**: Smaller, focused classes are easier to understand and modify
4. **Reusability**: Services and ViewModels can be shared across multiple views
5. **Data Binding**: Automatic UI updates when properties change
6. **Designer-Friendly**: XAML designers can work independently from developers

## Windows Edge Light MVVM Architecture

### Layer Overview

```
┌─────────────────────────────────────────────────────────┐
│                     View Layer                          │
│  MainWindow.xaml + MainWindow.xaml.cs                  │
│  ControlWindow.xaml + ControlWindow.xaml.cs            │
│  - XAML UI definitions with data bindings              │
│  - Minimal code-behind (only P/Invoke, HWND management)│
└────────────────────┬────────────────────────────────────┘
                     │ Data Binding & Commands
┌────────────────────▼────────────────────────────────────┐
│                  ViewModel Layer                        │
│  MainViewModel, ControlWindowViewModel                 │
│  - Exposes properties for data binding                 │
│  - Implements INotifyPropertyChanged                   │
│  - Provides ICommand implementations                   │
│  - Orchestrates service calls                          │
└────────────────────┬────────────────────────────────────┘
                     │ Service Calls
┌────────────────────▼────────────────────────────────────┐
│                   Service Layer                         │
│  MonitorService, HotkeyService, MouseHookService, etc. │
│  - Encapsulates specific functionality                 │
│  - Can be tested independently                         │
│  - Provides clean interfaces                           │
└────────────────────┬────────────────────────────────────┘
                     │ Data Access
┌────────────────────▼────────────────────────────────────┐
│                    Model Layer                          │
│  MonitorWindowContext, LightSettings, etc.             │
│  - Plain data objects (POCOs)                          │
│  - No business logic                                   │
└─────────────────────────────────────────────────────────┘
```

### Directory Structure

```
WindowsEdgeLight/
├── ViewModels/
│   ├── ViewModelBase.cs          # Base class with INotifyPropertyChanged
│   ├── MainViewModel.cs          # Main window business logic
│   └── ControlWindowViewModel.cs # Control window state
├── Services/
│   ├── Interfaces/
│   │   ├── IMonitorService.cs
│   │   ├── IHotkeyService.cs
│   │   ├── IMouseHookService.cs
│   │   ├── INotifyIconService.cs
│   │   └── IGeometryService.cs
│   ├── MonitorService.cs         # Screen enumeration, DPI, multi-monitor
│   ├── HotkeyService.cs          # Global hotkey registration
│   ├── MouseHookService.cs       # Mouse tracking, hole punch effect
│   ├── NotifyIconService.cs      # System tray icon management
│   └── GeometryService.cs        # Frame geometry calculations
├── Models/
│   ├── MonitorWindowContext.cs   # Monitor window data
│   └── LightSettings.cs          # Light configuration
├── Commands/
│   └── RelayCommand.cs           # ICommand implementation
├── MainWindow.xaml               # View with data bindings
├── MainWindow.xaml.cs            # Thin code-behind
├── ControlWindow.xaml            # Control panel view
└── ControlWindow.xaml.cs         # Thin code-behind
```

## Key Architectural Patterns

### 1. ViewModelBase with INotifyPropertyChanged

```csharp
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
            
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
```

**Purpose**: Provides automatic property change notification for data binding.

### 2. RelayCommand for ICommand Implementation

```csharp
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;
    
    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }
    
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
```

**Purpose**: Enables XAML button clicks to trigger ViewModel methods via data binding.

### 3. Service Interfaces for Dependency Injection

```csharp
public interface IMonitorService
{
    Screen[] GetAvailableMonitors();
    Screen GetPrimaryMonitor();
    (double scaleX, double scaleY) GetDpiForScreen(Screen screen);
    void CreateAdditionalMonitorWindow(Screen screen);
    void CloseAdditionalMonitorWindows();
}
```

**Purpose**: Enables testing with mock services and decouples implementation from interface.

### 4. Data Binding in XAML

```xml
<!-- Before: Event handler in code-behind -->
<Button Click="BrightnessUp_Click" Content="🔆" />

<!-- After: Command binding to ViewModel -->
<Button Command="{Binding IncreaseBrightnessCommand}" Content="🔆" />
```

```xml
<!-- Before: Property set in code-behind -->
<Path x:Name="EdgeLightBorder" Opacity="1.0" />

<!-- After: Bound to ViewModel property -->
<Path Opacity="{Binding CurrentOpacity}" />
```

**Purpose**: Automatic synchronization between UI and ViewModel properties.

## Implementation Phases

### Phase 1: Core Infrastructure (Completed)
✅ Created `ViewModels/ViewModelBase.cs`
✅ Created `Commands/RelayCommand.cs`
✅ Set up folder structure

### Phase 2: Extract Business Logic to MainViewModel
- Move light state properties (IsLightOn, CurrentOpacity, ColorTemperature)
- Implement commands (ToggleLight, IncreaseBrightness, DecreaseBrightness, etc.)
- Extract business logic methods

### Phase 3: Create Service Layer
- Implement MonitorService for screen management
- Implement HotkeyService for global shortcuts
- Implement MouseHookService for cursor tracking
- Implement NotifyIconService for system tray
- Implement GeometryService for calculations

### Phase 4: Refactor Views
- Update MainWindow.xaml with data bindings
- Slim down MainWindow.xaml.cs to view-only logic
- Update ControlWindow.xaml with bindings
- Update ControlWindow.xaml.cs

### Phase 5: Testing
- Create unit tests for ViewModels
- Create unit tests for Services
- Manual testing on Windows

### Phase 6: Documentation
- Update architecture docs (this file)
- Add inline XML documentation
- Update developer guide

## Data Binding Examples

### Property Binding

**ViewModel:**
```csharp
public class MainViewModel : ViewModelBase
{
    private double _currentOpacity = 1.0;
    
    public double CurrentOpacity
    {
        get => _currentOpacity;
        set => SetProperty(ref _currentOpacity, value);
    }
}
```

**XAML:**
```xml
<Path Opacity="{Binding CurrentOpacity}" />
```

### Command Binding

**ViewModel:**
```csharp
public class MainViewModel : ViewModelBase
{
    public ICommand IncreaseBrightnessCommand { get; }
    
    public MainViewModel()
    {
        IncreaseBrightnessCommand = new RelayCommand(
            _ => IncreaseBrightness(),
            _ => CurrentOpacity < MaxOpacity
        );
    }
    
    private void IncreaseBrightness()
    {
        CurrentOpacity = Math.Min(MaxOpacity, CurrentOpacity + OpacityStep);
    }
}
```

**XAML:**
```xml
<Button Command="{Binding IncreaseBrightnessCommand}" Content="🔆" />
```

### Visibility Binding with Converter

**ViewModel:**
```csharp
public bool IsLightOn
{
    get => _isLightOn;
    set => SetProperty(ref _isLightOn, value);
}
```

**XAML:**
```xml
<Path Visibility="{Binding IsLightOn, Converter={StaticResource BooleanToVisibilityConverter}}" />
```

## Testing Strategy

### Unit Testing ViewModels

```csharp
[TestClass]
public class MainViewModelTests
{
    [TestMethod]
    public void IncreaseBrightness_WhenBelowMax_IncreasesOpacity()
    {
        // Arrange
        var viewModel = new MainViewModel();
        var initialOpacity = viewModel.CurrentOpacity;
        
        // Act
        viewModel.IncreaseBrightnessCommand.Execute(null);
        
        // Assert
        Assert.IsTrue(viewModel.CurrentOpacity > initialOpacity);
    }
    
    [TestMethod]
    public void IncreaseBrightness_WhenAtMax_StaysAtMax()
    {
        // Arrange
        var viewModel = new MainViewModel();
        viewModel.CurrentOpacity = 1.0; // Max
        
        // Act
        viewModel.IncreaseBrightnessCommand.Execute(null);
        
        // Assert
        Assert.AreEqual(1.0, viewModel.CurrentOpacity);
    }
}
```

### Unit Testing Services

```csharp
[TestClass]
public class MonitorServiceTests
{
    [TestMethod]
    public void GetAvailableMonitors_ReturnsAtLeastOne()
    {
        // Arrange
        var service = new MonitorService();
        
        // Act
        var monitors = service.GetAvailableMonitors();
        
        // Assert
        Assert.IsTrue(monitors.Length > 0);
    }
}
```

### Integration Testing

```csharp
[TestClass]
public class MainViewModelIntegrationTests
{
    [TestMethod]
    public void ToggleAllMonitors_WithMultipleMonitors_CreatesAdditionalWindows()
    {
        // Arrange
        var monitorService = new MonitorService();
        var viewModel = new MainViewModel(monitorService);
        
        // Act
        viewModel.ToggleAllMonitorsCommand.Execute(null);
        
        // Assert
        Assert.IsTrue(viewModel.ShowOnAllMonitors);
        // Verify additional windows created
    }
}
```

## Best Practices

### 1. Keep ViewModels UI-Agnostic
❌ **Bad**: ViewModel directly manipulates UI elements
```csharp
// In ViewModel - DON'T DO THIS
public void UpdateUI()
{
    EdgeLightBorder.Opacity = _currentOpacity;
}
```

✅ **Good**: ViewModel exposes properties, View binds to them
```csharp
// In ViewModel
public double CurrentOpacity
{
    get => _currentOpacity;
    set => SetProperty(ref _currentOpacity, value);
}

// In XAML
<Path Opacity="{Binding CurrentOpacity}" />
```

### 2. Use Services for External Dependencies
❌ **Bad**: ViewModel directly calls P/Invoke
```csharp
// In ViewModel - DON'T DO THIS
[DllImport("user32.dll")]
private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
```

✅ **Good**: Service handles P/Invoke, ViewModel uses service
```csharp
// In Service
public class HotkeyService : IHotkeyService
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    
    public void RegisterHotkey(IntPtr hwnd, int id, uint modifiers, uint vk)
    {
        RegisterHotKey(hwnd, id, modifiers, vk);
    }
}

// In ViewModel
public MainViewModel(IHotkeyService hotkeyService)
{
    _hotkeyService = hotkeyService;
}
```

### 3. Use Commands Instead of Event Handlers
❌ **Bad**: Code-behind event handlers
```csharp
// In MainWindow.xaml.cs - AVOID THIS
private void BrightnessUp_Click(object sender, RoutedEventArgs e)
{
    currentOpacity = Math.Min(MaxOpacity, currentOpacity + OpacityStep);
    EdgeLightBorder.Opacity = currentOpacity;
}
```

✅ **Good**: Commands in ViewModel
```csharp
// In ViewModel
public ICommand IncreaseBrightnessCommand { get; }

public MainViewModel()
{
    IncreaseBrightnessCommand = new RelayCommand(_ => IncreaseBrightness());
}
```

### 4. Minimize Code-Behind
✅ **Acceptable in code-behind**:
- Window initialization
- P/Invoke declarations (or move to services)
- HWND management
- Attaching DataContext

❌ **Should be in ViewModel**:
- Business logic
- State management
- Command implementations
- Property change notifications

## Performance Considerations

### 1. Avoid Excessive PropertyChanged Events
Use `SetProperty` helper that only raises events when value actually changes:

```csharp
protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
{
    if (EqualityComparer<T>.Default.Equals(field, value))
        return false; // No change, don't raise event
        
    field = value;
    OnPropertyChanged(propertyName);
    return true;
}
```

### 2. Use Weak Event Patterns for Long-Lived Objects
For event subscriptions that might cause memory leaks:

```csharp
WeakEventManager<MonitorService, EventArgs>
    .AddHandler(monitorService, nameof(MonitorService.MonitorsChanged), OnMonitorsChanged);
```

### 3. Dispose Resources Properly
Services should implement IDisposable:

```csharp
public class HotkeyService : IHotkeyService, IDisposable
{
    public void Dispose()
    {
        UnregisterAllHotkeys();
    }
}
```

## Migration Path

For developers working with the old code:

| Old Code | New Code | Location |
|----------|----------|----------|
| `MainWindow.isLightOn` | `MainViewModel.IsLightOn` | Property |
| `MainWindow.ToggleLight()` | `MainViewModel.ToggleLightCommand` | Command |
| `MainWindow.IncreaseBrightness()` | `MainViewModel.IncreaseBrightnessCommand` | Command |
| `MainWindow.MoveToNextMonitor()` | `MainViewModel.MoveToNextMonitorCommand` | Command |
| `MainWindow.CreateFrameGeometry()` | `GeometryService.CreateFrameGeometry()` | Service |
| `MainWindow.SetupNotifyIcon()` | `NotifyIconService.Initialize()` | Service |
| Direct property setting | Data binding | XAML |
| `Button.Click` event | `Button.Command` binding | XAML |

## Troubleshooting

### Issue: UI Not Updating
**Symptom**: Property changes don't reflect in UI

**Solution**: Ensure ViewModel implements INotifyPropertyChanged and raises PropertyChanged event:
```csharp
public double CurrentOpacity
{
    get => _currentOpacity;
    set
    {
        _currentOpacity = value;
        OnPropertyChanged(); // Must call this!
    }
}
```

### Issue: Commands Not Executing
**Symptom**: Button clicks don't trigger ViewModel methods

**Solution**: 
1. Check DataContext is set: `<Window DataContext="{Binding MainViewModel}">`
2. Verify command is public property: `public ICommand ToggleLightCommand { get; }`
3. Check binding: `<Button Command="{Binding ToggleLightCommand}" />`

### Issue: NullReferenceException in ViewModel
**Symptom**: Service is null when ViewModel tries to use it

**Solution**: Use dependency injection and ensure services are initialized:
```csharp
public MainViewModel(IMonitorService monitorService)
{
    _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
}
```

## Additional Resources

- [Microsoft MVVM Documentation](https://docs.microsoft.com/en-us/dotnet/architecture/maui/mvvm)
- [WPF MVVM Pattern](https://docs.microsoft.com/en-us/archive/msdn-magazine/2009/february/patterns-wpf-apps-with-the-model-view-viewmodel-design-pattern)
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/) - Alternative to custom implementations
- [Prism Framework](https://prismlibrary.com/) - Full-featured MVVM framework

## Conclusion

The MVVM refactoring of Windows Edge Light transforms a monolithic 1,224-line class into a well-structured, testable, and maintainable application. By separating concerns into Views, ViewModels, Services, and Models, we achieve:

- **Better code organization**: Each class has a single responsibility
- **Improved testability**: Business logic can be unit tested
- **Enhanced maintainability**: Changes are localized and predictable
- **Greater flexibility**: Services can be swapped or mocked for testing

This architecture provides a solid foundation for future enhancements and demonstrates professional WPF development practices.
