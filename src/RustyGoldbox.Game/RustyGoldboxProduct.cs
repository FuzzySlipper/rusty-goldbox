using Rusty.Engine;

namespace RustyGoldbox.Game;

/// <summary>
/// The Engine product: runs Core campaigns from the module bundles under
/// Engine input and persistence, and publishes the session as a debug
/// projection for the DOM readout.
/// </summary>
public sealed class RustyGoldboxProduct : IEngineProduct
{
    private const string UiStreamId = "rusty-goldbox";
    private const string UiContract = "rusty.goldbox.session";
    private readonly IEngineContext _engine;
    private readonly UiStream _uiStream;
    private readonly GameSession _session;
    private ulong _uiSequence;

    public RustyGoldboxProduct(ProductCreateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _engine = context.Engine;
        _session = new GameSession(new ModuleLibrary(() => OpenBundles(context.Content)));
        _uiStream = _engine.Ui.OpenStream(new UiStreamRequest(UiStreamId, UiContract));
    }

    public void Start()
    {
        _session.Refresh();
        Publish();
    }

    public ProductUpdateResult Update(ProductUpdate update)
    {
        bool changed = false;
        foreach (ProductInputEvent input in update.Input)
        {
            changed |= GameCommands.Apply(_session, _engine, input);
        }

        if (changed)
        {
            Publish();
        }

        return ProductUpdateResult.None;
    }

    public void Pause()
    {
    }

    public void Resume()
    {
    }

    public void Restart()
    {
        _session.Quit();
        _session.Refresh();
        Publish();
    }

    public void Shutdown()
    {
    }

    public void Dispose() => _uiStream.Dispose();

    private static List<ProductContentBundle> OpenBundles(ProductContent content)
    {
        List<ProductContentBundle> bundles = [];
        foreach (ContentBundleInfo info in content.ListBundles().Span)
        {
            bundles.Add(content.OpenBundle(info.Id));
        }

        return bundles;
    }

    private void Publish()
    {
        UiValue value = SessionProjection.ToUiValue(SessionProjection.Build(_session));
        _engine.Ui.PublishProjection(new UiProjection(_uiStream, ++_uiSequence, value));
    }
}
