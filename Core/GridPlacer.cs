using Xabbo.Core;
using Xabbo.Core.Game;

namespace GridAlignment.Core;

public class GridPlacer
{
    private const int HALF_W = 32;
    private const int HALF_H = 16;
    private const double BASE_OFFSET = 3.59375;

    public static double calcwallheight(IRoom room, WallLocation loc)
    {
        var plan = room.FloorPlan;
        var entry = room.Entry;
        double result = BASE_OFFSET;

        int highesttile = 0;
        for (int y = 0; y < plan.Size.Y; y++)
        {
            for (int x = 0; x < plan.Size.X; x++)
            {
                int h = plan[x, y];
                if (h > highesttile) highesttile = h;
            }
        }

        if (plan.WallHeight != -1)
        {
            result = (entry == (loc.Wall.X, loc.Wall.Y))
                ? result + plan.WallHeight
                : result + (plan.WallHeight * 2);
        }
        else if (entry != (loc.Wall.X, loc.Wall.Y))
        {
            result += highesttile;
        }

        int tileh = -1;
        if (loc.Wall.X >= 0 && loc.Wall.Y >= 0 && loc.Wall.X < plan.Size.X && loc.Wall.Y < plan.Size.Y)
        {
            tileh = plan[loc.Wall.X, loc.Wall.Y];
        }
        else if (loc.Wall.X < 0 || loc.Wall.Y < 0 || loc.Wall.X >= plan.Size.X || loc.Wall.Y >= plan.Size.Y)
        {
            tileh = 0;
        }

        if (loc.Wall.X == entry.X && loc.Wall.Y == entry.Y)
        {
            tileh = -1;
        }

        return (tileh >= 0) ? tileh : result;
    }

    public static (int screenx, int screeny) calcitemloc(IRoom room, WallLocation loc)
    {
        var plan = room.FloorPlan;
        int unitsize = 64 / plan.Scale;
        double heightoffset = calcwallheight(room, loc);

        int screenx = (loc.Wall.X - loc.Wall.Y) * HALF_W;
        int screeny = (loc.Wall.X + loc.Wall.Y) * HALF_H;

        screenx += loc.Offset.X * unitsize;
        screeny += loc.Offset.Y * unitsize;

        if (loc.Orientation == 'r')
        {
            screenx -= HALF_W;
        }

        screeny -= (int)(heightoffset * HALF_W);

        return (screenx, screeny);
    }

    public static List<WallLocation> creategrid(
        IRoom room,
        WallLocation startloc,
        int columns,
        int rows,
        int spacingx,
        int spacingy,
        int rowoffset,
        int zindex)
    {
        var locations = new List<WallLocation>();
        var plan = room.FloorPlan;
        int roomscale = plan.Scale;
        int unitsize = 64 / roomscale;
        int lxstep = HALF_W / unitsize;
        int maxy = plan.Size.Y;

        var startscreen = calcitemloc(room, startloc);
        double basevisualy = startscreen.screeny;

        int startwx = startloc.Wall.X;
        int startwy = startloc.Wall.Y;
        int startlx = startloc.Offset.X;
        char orientation = startloc.Orientation;

        if (startwy > 0)
        {
            startwx = startwx - startwy;
            startwy = 0;
        }

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                int lx = startlx + (col * spacingx);

                int wx = startwx;

                while (lx >= lxstep) { wx++; lx -= lxstep; }
                while (lx < 0) { wx--; lx += lxstep; }

                double targetvisualy = basevisualy + (row * spacingy + col * rowoffset) * unitsize;

                int colzindex = zindex - col;
                int targetwy = Math.Max(0, Math.Min(colzindex, maxy));
                int finalwx = wx + targetwy;
                int finalwy = targetwy;

                var temploc = new WallLocation(new Point(finalwx, finalwy), new Point(lx, 0), orientation);
                double heightoffset = calcwallheight(room, temploc);

                int tilescreny = (finalwx + finalwy) * HALF_H;
                double calculatedly = (targetvisualy - tilescreny + heightoffset * HALF_W) / unitsize;
                int finaly = (int)Math.Round(calculatedly);

                var loc = new WallLocation(
                    new Point(finalwx, finalwy),
                    new Point(lx, finaly),
                    orientation
                );

                locations.Add(loc);
            }
        }

        return locations;
    }
}
