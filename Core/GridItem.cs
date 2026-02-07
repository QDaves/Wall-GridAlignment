using Xabbo.Core;

namespace GridAlignment.Core;

public class GridItem
{
    public long Id { get; set; }
    public int Col { get; set; }
    public int Row { get; set; }
    public string LocationStr { get; set; } = "";
    public WallLocation TargetLocation { get; set; }
    public IWallItem? Item { get; set; }
}
