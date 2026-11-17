namespace BotnetMiningDetector.App.Common;

// estado compartido de la interfaz
public sealed class UiInteractionState
{
    private string _activeTab = "dashboard";
    private long? _selectedAlertId;
    private readonly Dictionary<string, string> _filters = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hiddenColumns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SortState> _sortStates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["processes"] = new("cpu", false),
        ["alerts"] = new("date", false),
        ["baseline"] = new("count", false),
        ["scenarios"] = new("start", false)
    };

    public event Action? Changed;

    public string ActiveTab => _activeTab;
    public long? SelectedAlertId => _selectedAlertId;
    // obtiene el filtro de una tabla
    public string GetFilter(string key) => _filters.TryGetValue(key, out var value) ? value : string.Empty;

    // indica si una columna es visible
    public bool IsColumnVisible(string key) => !_hiddenColumns.Contains(key);

    // obtiene el orden actual de una tabla
    public SortState GetSortState(string table) => _sortStates.TryGetValue(table, out var state) ? state : new(string.Empty, true);

    // selecciona la vista activa
    public void SelectTab(string tab)
    {
        if (string.IsNullOrWhiteSpace(tab) || _activeTab.Equals(tab, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _activeTab = tab;
        Changed?.Invoke();
    }

    // selecciona una alerta
    public void SelectAlert(long alertId)
    {
        _selectedAlertId = alertId;
        Changed?.Invoke();
    }

    // actualiza un filtro de tabla
    public void SetFilter(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        value ??= string.Empty;
        if (_filters.TryGetValue(key, out var current) && current == value)
        {
            return;
        }

        _filters[key] = value;
        Changed?.Invoke();
    }

    // cambia la visibilidad de una columna
    public void SetColumnVisible(string key, bool isVisible)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var changed = isVisible ? _hiddenColumns.Remove(key) : _hiddenColumns.Add(key);
        if (changed)
        {
            Changed?.Invoke();
        }
    }

    // cambia el orden de una columna
    public void ToggleSort(string table, string column)
    {
        if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(column))
        {
            return;
        }

        if (_sortStates.TryGetValue(table, out var current) && current.Column.Equals(column, StringComparison.OrdinalIgnoreCase))
        {
            _sortStates[table] = current with { Ascending = !current.Ascending };
        }
        else
        {
            _sortStates[table] = new SortState(column, true);
        }

        Changed?.Invoke();
    }

    // estado de orden de una tabla
    public sealed record SortState(string Column, bool Ascending);
}
