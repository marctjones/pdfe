using PdfEditor.Redaction.Operators.TextObject;
using PdfEditor.Redaction.Operators.TextPositioning;
using PdfEditor.Redaction.Operators.TextShowing;
using PdfEditor.Redaction.Operators.TextState;

namespace PdfEditor.Redaction.Operators;

/// <summary>
/// Registry for PDF operator handlers.
/// Allows modular addition of new operators without modifying existing code.
/// </summary>
public class OperatorRegistry
{
    private readonly Dictionary<string, IOperatorHandler> _handlers = new();

    /// <summary>
    /// Create a registry with all default operator handlers.
    /// </summary>
    public static OperatorRegistry CreateDefault()
    {
        var registry = new OperatorRegistry();

        // Text object operators
        registry.Register(new BtOperatorHandler());
        registry.Register(new EtOperatorHandler());

        // Text state operators
        registry.Register(new TfOperatorHandler());
        registry.Register(new TlOperatorHandler());

        // Text positioning operators
        registry.Register(new TmOperatorHandler());
        registry.Register(new TdUpperOperatorHandler());
        registry.Register(new TdLowerOperatorHandler());
        registry.Register(new TStarOperatorHandler());

        // Text showing operators
        registry.Register(new TjOperatorHandler());
        registry.Register(new TjUpperOperatorHandler());

        return registry;
    }

    /// <summary>
    /// Create an empty registry (for testing specific operators).
    /// </summary>
    public static OperatorRegistry CreateEmpty() => new();

    /// <summary>
    /// Register an operator handler.
    /// </summary>
    public void Register(IOperatorHandler handler)
    {
        _handlers[handler.OperatorName] = handler;
    }

    /// <summary>
    /// Unregister an operator handler.
    /// </summary>
    public bool Unregister(string operatorName)
    {
        return _handlers.Remove(operatorName);
    }

    /// <summary>
    /// Get handler for an operator, if registered.
    /// </summary>
    public IOperatorHandler? GetHandler(string operatorName)
    {
        return _handlers.TryGetValue(operatorName, out var handler) ? handler : null;
    }

    /// <summary>
    /// Check if a handler is registered for an operator.
    /// </summary>
    public bool HasHandler(string operatorName)
    {
        return _handlers.ContainsKey(operatorName);
    }

    /// <summary>
    /// Get all registered operator names.
    /// </summary>
    public IEnumerable<string> RegisteredOperators => _handlers.Keys;

    /// <summary>
    /// Number of registered handlers.
    /// </summary>
    public int Count => _handlers.Count;
}
