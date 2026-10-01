using System.Text;
using Rusty.Engine;

namespace RustyGoldbox.Game;

/// <summary>
/// Engine product shell. It publishes a status readout until the campaign
/// runtime from Core is wired in (design phase 5).
/// </summary>
public sealed class RustyGoldboxProduct : IEngineProduct
{
    private const string UiStreamId = "rusty-goldbox";
    private const string UiContract = "rusty.goldbox.status";
    private const string StatusKey = "status";
    private const string IdleStatus = "No campaign loaded";

    private readonly IEngineContext _engine;
    private readonly UiStream _uiStream;
    private ulong _uiSequence;

    public RustyGoldboxProduct(ProductCreateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _engine = context.Engine;
        _uiStream = _engine.Ui.OpenStream(new UiStreamRequest(UiStreamId, UiContract));
    }

    public void Start() => PublishStatus(IdleStatus);

    public ProductUpdateResult Update(ProductUpdate update) => ProductUpdateResult.None;

    public void Pause()
    {
    }

    public void Resume()
    {
    }

    public void Restart() => PublishStatus(IdleStatus);

    public void Shutdown()
    {
    }

    public void Dispose() => _uiStream.Dispose();

    private void PublishStatus(string status)
    {
        byte[] text = Encoding.UTF8.GetBytes(StatusKey + status);
        uint keyLength = (uint)Encoding.UTF8.GetByteCount(StatusKey);
        uint statusLength = (uint)text.Length - keyLength;
        StructuredValueNode[] nodes =
        [
            new(StructuredValueKind.Object, 0, 0, 0, 0, 0, 0, 0, 1),
            new(StructuredValueKind.String, 0, 0, 0, keyLength, keyLength, statusLength, 0, 0),
        ];
        UiValue value = new(nodes, new uint[] { 1 }, 0, text);
        _engine.Ui.PublishProjection(new UiProjection(_uiStream, ++_uiSequence, value));
    }
}
