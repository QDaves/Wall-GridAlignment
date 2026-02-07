using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Xabbo;
using Xabbo.Core;
using Xabbo.Core.Game;
using Xabbo.Messages.Flash;
using GridAlignment.Core;

namespace GridAlignment.UI;

public class IdGroup
{
    public string Name { get; set; } = "";
    public List<long> Ids { get; set; } = new();
    public override string ToString() => Name;
}

public partial class MainWindow : Window
{
    private const int HALF_W = 32;
    private const int HALF_H = 16;
    private const double BASE_OFFSET = 3.59375;

    private Extension ext;
    private WallLocation? startlocation;
    private char orientation = 'r';
    private bool capturingids = false;
    private bool ignoregroupselect = false;
    private bool ignoreidchange = false;
    private ObservableCollection<GridItem> griditems = new();
    private List<long> importedids = new();
    private List<IdGroup> groups = new();
    private DispatcherTimer? previewtimer;
    private bool previewpending = false;
    private bool sortascending = true;
    private int highesttile;

    public MainWindow(Extension extension)
    {
        InitializeComponent();
        ext = extension;
        setup();
    }

    private void setup()
    {
        dgvitems.ItemsSource = griditems;

        btnminimize.Click += (s, e) => WindowState = WindowState.Minimized;
        btnmaximize.Click += (s, e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        btnclose.Click += (s, e) => Close();

        btncaptureids.Click += oncaptureids;
        btnclear.Click += onclear;
        btnmoveall.Click += onmoveall;
        btnimportfile.Click += onimportfile;
        btnexport.Click += onexport;
        btnimportfurni.Click += onimportfurni;
        btnsortids.Click += onsortids;
        cmbgroups.SelectionChanged += ongroupselected;

        btnposup.Click += (s, e) => { stopcapture(); movestartpos(0, -getstep()); };
        btnposdown.Click += (s, e) => { stopcapture(); movestartpos(0, getstep()); };
        btnposleft.Click += (s, e) => { stopcapture(); movestartpos(-getstep(), 0); };
        btnposright.Click += (s, e) => { stopcapture(); movestartpos(getstep(), 0); };
        btnrotate.Click += onrotate;

        btncolsminus.Click += (s, e) => adjustvalue(txtcols, -1, 1, 99);
        btncolsplus.Click += (s, e) => adjustvalue(txtcols, 1, 1, 99);
        btnrowsminus.Click += (s, e) => adjustvalue(txtrows, -1, 1, 99);
        btnrowsplus.Click += (s, e) => adjustvalue(txtrows, 1, 1, 99);
        btnspacingxminus.Click += (s, e) => adjustvalue(txtspacingx, -1, 1, 99);
        btnspacingxplus.Click += (s, e) => adjustvalue(txtspacingx, 1, 1, 99);
        btnspacingyminus.Click += (s, e) => adjustvalue(txtspacingy, -1, 1, 99);
        btnspacingyplus.Click += (s, e) => adjustvalue(txtspacingy, 1, 1, 99);
        btnoffsetminus.Click += (s, e) => adjustvalue(txtrowoffset, -1, -99, 99);
        btnoffsetplus.Click += (s, e) => adjustvalue(txtrowoffset, 1, -99, 99);
        btnzminus.Click += (s, e) => adjustvalue(txtzindex, -1, -99, 99);
        btnzplus.Click += (s, e) => adjustvalue(txtzindex, 1, -99, 99);
        chkontop.Checked += (s, e) => Topmost = true;
        chkontop.Unchecked += (s, e) => Topmost = false;

        txtcols.TextChanged += onsettingchanged;
        txtrows.TextChanged += onsettingchanged;
        txtspacingx.TextChanged += onsettingchanged;
        txtspacingy.TextChanged += onsettingchanged;
        txtrowoffset.TextChanged += onsettingchanged;
        txtzindex.TextChanged += onsettingchanged;
        txtids.TextChanged += onidschanged;

        previewtimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        previewtimer.Tick += (s, e) =>
        {
            previewtimer.Stop();
            if (previewpending)
            {
                previewpending = false;
                dopreview();
            }
        };

        detectgroups();

        ext.Room.Entered += e => Dispatcher.Invoke(() =>
        {
            refreshfurnilist();
            updatehighesttile();
            detectgroups();
        });
        ext.Room.WallItemAdded += e => Dispatcher.Invoke(() => { refreshfurnilist(); detectgroups(); onwallitemadded(e.Item); });
        ext.Room.WallItemRemoved += e => Dispatcher.Invoke(() => { refreshfurnilist(); detectgroups(); });
        ext.Room.WallItemUpdated += e => Dispatcher.Invoke(() => onwallitemupdate(e.Item));
    }

    private void updatehighesttile()
    {
        if (ext.Room?.Room == null) return;
        var plan = ext.Room.Room.FloorPlan;
        highesttile = 0;
        for (int y = 0; y < plan.Size.Y; y++)
        {
            for (int x = 0; x < plan.Size.X; x++)
            {
                int h = plan[x, y];
                if (h > highesttile) highesttile = h;
            }
        }
    }

    private double calcwallheight(IRoom room, WallLocation loc)
    {
        var plan = room.FloorPlan;
        var entry = room.Entry;
        double result = BASE_OFFSET;

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

    private (double x, double y) calcitemloc(IRoom room, WallLocation loc)
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

    private int getstep()
    {
        if (int.TryParse(txtstep.Text, out int step) && step > 0)
            return step;
        return 15;
    }

    private void stopcapture()
    {
        if (capturingids)
        {
            capturingids = false;
            btncaptureids.Content = "Capture";
            btncaptureids.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xe8, 0xee, 0xf8));
        }
    }

    private void adjustvalue(TextBox txt, int delta, int min, int max)
    {
        if (int.TryParse(txt.Text, out int val))
        {
            val = Math.Clamp(val + delta, min, max);
            txt.Text = val.ToString();
        }
    }

    private void onsettingchanged(object? sender, EventArgs e)
    {
        if (startlocation == null) return;
        if (importedids.Count == 0) return;
        if (capturingids) return;

        previewpending = true;
        previewtimer?.Stop();
        previewtimer?.Start();
    }

    private void onidschanged(object? sender, EventArgs e)
    {
        if (ignoreidchange) return;
        if (capturingids) return;

        var lines = txtids.Text.Split('\n', '\r').Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        var newids = new List<long>();

        foreach (var line in lines)
        {
            if (long.TryParse(line.Trim(), out long id))
                newids.Add(id);
        }

        if (newids.SequenceEqual(importedids)) return;

        importedids = newids;

        if (importedids.Count > 0)
            updatestartfromfirstid();

        onsettingchanged(null, EventArgs.Empty);
    }

    private void movestartpos(int dx, int dy)
    {
        if (startlocation == null)
        {
            setstatus("select group or import IDs first");
            return;
        }

        if (ext.Room?.Room == null) return;

        var room = ext.Room.Room;
        var plan = room.FloorPlan;
        int unitsize = 64 / plan.Scale;
        int lxstep = HALF_W / unitsize;

        var origloc = startlocation.Value;
        var (_, origvisualy) = calcitemloc(room, origloc);

        int wx = origloc.Wall.X;
        int wy = origloc.Wall.Y;
        int lx = origloc.Offset.X + dx;

        while (lx < 0) { wx--; lx += lxstep; }
        while (lx >= lxstep) { wx++; lx -= lxstep; }

        int finalwx = wx;
        int finalwy = wy;

        if (wy > 0)
        {
            finalwx = wx - wy;
            finalwy = 0;
        }

        double targetvisualy = origvisualy + dy * unitsize;

        var temploc = new WallLocation(new Xabbo.Core.Point(finalwx, finalwy), new Xabbo.Core.Point(lx, 0), orientation);
        double heightoffset = calcwallheight(room, temploc);

        int tilescreny = (finalwx + finalwy) * HALF_H;
        double calculatedly = (targetvisualy - tilescreny + heightoffset * HALF_W) / unitsize;
        int finaly = (int)Math.Round(calculatedly);

        startlocation = new WallLocation(
            new Xabbo.Core.Point(finalwx, finalwy),
            new Xabbo.Core.Point(lx, finaly),
            orientation
        );

        lblstatus.Content = $"Start: {startlocation.Value}";
        onsettingchanged(null, EventArgs.Empty);
    }

    private void onrotate(object sender, RoutedEventArgs e)
    {
        orientation = orientation == 'r' ? 'l' : 'r';

        if (startlocation != null)
        {
            var loc = startlocation.Value;
            startlocation = new WallLocation(loc.Wall, loc.Offset, orientation);
            setstatus($"rotated to {char.ToUpper(orientation)}");
        }

        onsettingchanged(null, EventArgs.Empty);
    }

    private void onwallitemadded(IWallItem item)
    {
        if (capturingids)
        {
            addcapturedid(item.Id);
        }
    }

    private void onwallitemupdate(IWallItem item)
    {
        if (capturingids)
        {
            addcapturedid(item.Id);
        }
    }

    private void oncaptureids(object sender, RoutedEventArgs e)
    {
        capturingids = !capturingids;
        btncaptureids.Content = capturingids ? "Stop" : "Capture";
        btncaptureids.Background = capturingids
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xf8, 0xe8, 0xe8))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xe8, 0xee, 0xf8));

        if (capturingids)
            setstatus("move/place items to capture");
        else
            setstatus($"captured {importedids.Count}");
    }

    private void addcapturedid(long id)
    {
        if (!importedids.Contains(id))
        {
            importedids.Add(id);
            ignoreidchange = true;
            txtids.Text = string.Join("\n", importedids);
            ignoreidchange = false;
            updatestartfromfirstid();
            setstatus($"captured ID: {id} ({importedids.Count} total)");
        }
    }

    private void refreshfurnilist()
    {
        if (ext.Room?.Room == null) return;

        var names = new HashSet<string>();
        foreach (var item in ext.Room.Room.WallItems)
        {
            var name = item.GetName();
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        }

        var sorted = names.OrderBy(x => x).ToList();
        cmbfurni.ItemsSource = sorted;

        if (sorted.Count > 0 && cmbfurni.SelectedItem == null)
            cmbfurni.SelectedIndex = 0;

        setstatus($"found {sorted.Count} types");
    }

    private void onclear(object sender, RoutedEventArgs e)
    {
        griditems.Clear();
        importedids.Clear();
        startlocation = null;

        lblstatus.Content = "Start: -";
        lblitemcount.Content = "Items: 0";
        ignoreidchange = true;
        txtids.Text = "";
        ignoreidchange = false;

        detectgroups();
        setstatus("cleared");
    }

    private void onimportfurni(object sender, RoutedEventArgs e)
    {
        if (ext.Room?.Room == null)
        {
            setstatus("not in room");
            return;
        }

        if (cmbfurni.SelectedItem == null)
        {
            setstatus("select furniture first");
            return;
        }

        string furniname = cmbfurni.SelectedItem.ToString() ?? "";
        var ids = ext.Room.Room.WallItems
            .Where(x => x.GetName()?.Equals(furniname, StringComparison.OrdinalIgnoreCase) == true)
            .OrderBy(x => x.Id)
            .Select(x => (long)x.Id)
            .ToList();

        if (ids.Count == 0)
        {
            setstatus($"no items: {furniname}");
            return;
        }

        importedids.Clear();
        importedids.AddRange(ids);
        ignoreidchange = true;
        txtids.Text = string.Join("\n", importedids);
        ignoreidchange = false;
        setstatus($"imported {importedids.Count} IDs");

        updatestartfromfirstid();
        onsettingchanged(null, EventArgs.Empty);
    }

    private string getprefix(long num)
    {
        var s = num.ToString();
        return s.Length <= 5 ? s : s.Substring(0, 5);
    }

    private void detectgroups()
    {
        ignoregroupselect = true;

        if (ext.Room?.Room == null)
        {
            cmbgroups.ItemsSource = new List<IdGroup> { new IdGroup { Name = "No room", Ids = new List<long>() } };
            cmbgroups.SelectedIndex = 0;
            setstatus("no room");
            ignoregroupselect = false;
            return;
        }

        var roomids = ext.Room.Room.WallItems.Select(x => (long)x.Id).OrderBy(x => x).ToList();

        if (roomids.Count == 0)
        {
            cmbgroups.ItemsSource = new List<IdGroup> { new IdGroup { Name = "No wall items", Ids = new List<long>() } };
            cmbgroups.SelectedIndex = 0;
            ignoregroupselect = false;
            return;
        }

        groups.Clear();

        var currentgroup = new List<long> { roomids[0] };
        var currentprefix = getprefix(roomids[0]);

        for (int i = 1; i < roomids.Count; i++)
        {
            var prefix = getprefix(roomids[i]);
            if (prefix == currentprefix)
            {
                currentgroup.Add(roomids[i]);
            }
            else
            {
                if (currentgroup.Count >= 3)
                {
                    groups.Add(new IdGroup
                    {
                        Name = $"Group {groups.Count + 1} ({currentgroup.Count} items)",
                        Ids = currentgroup.ToList()
                    });
                }
                currentgroup = new List<long> { roomids[i] };
                currentprefix = prefix;
            }
        }

        if (currentgroup.Count >= 3)
        {
            groups.Add(new IdGroup
            {
                Name = $"Group {groups.Count + 1} ({currentgroup.Count} items)",
                Ids = currentgroup.ToList()
            });
        }

        if (groups.Count > 0)
        {
            var displaylist = new List<IdGroup> { new IdGroup { Name = "Select detected groups...", Ids = new List<long>() } };
            displaylist.AddRange(groups);
            cmbgroups.ItemsSource = displaylist;
            cmbgroups.SelectedIndex = 0;
        }
        else
        {
            cmbgroups.ItemsSource = new List<IdGroup> { new IdGroup { Name = "No groups found", Ids = new List<long>() } };
            cmbgroups.SelectedIndex = 0;
        }

        ignoregroupselect = false;
    }

    private void ongroupselected(object sender, SelectionChangedEventArgs e)
    {
        if (ignoregroupselect) return;
        if (cmbgroups.SelectedItem is not IdGroup group) return;
        if (group.Ids.Count == 0) return;

        importedids = group.Ids.ToList();
        ignoreidchange = true;
        txtids.Text = string.Join("\n", importedids);
        ignoreidchange = false;
        updatestartfromfirstid();
        onsettingchanged(null, EventArgs.Empty);
    }

    private void updatestartfromfirstid()
    {
        if (importedids.Count == 0) return;
        if (ext.Room?.Room == null) return;

        var firstid = importedids[0];
        var item = ext.Room.Room.WallItems.FirstOrDefault(x => x.Id == firstid);
        if (item == null) return;

        startlocation = item.Location;
        orientation = item.Location.Orientation;
        setstatus($"start: {item.Location}");
    }

    private void onsortids(object sender, RoutedEventArgs e)
    {
        if (importedids.Count == 0)
        {
            setstatus("no IDs to sort");
            return;
        }

        if (sortascending)
            importedids = importedids.OrderBy(x => x).ToList();
        else
            importedids = importedids.OrderByDescending(x => x).ToList();

        sortascending = !sortascending;
        ignoreidchange = true;
        txtids.Text = string.Join("\n", importedids);
        ignoreidchange = false;
        setstatus(sortascending ? "sorted: high to low" : "sorted: low to high");
        onsettingchanged(null, EventArgs.Empty);
    }

    private void onimportfile(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Load Grid",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*"
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            var content = File.ReadAllText(dlg.FileName);
            var lines = content.Split('\n', '\r').Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

            griditems.Clear();
            importedids.Clear();

            foreach (var line in lines)
            {
                var parts = line.Split('\t');
                if (parts.Length >= 4 && long.TryParse(parts[0].Trim(), out long id))
                {
                    int col = int.TryParse(parts[1].Trim(), out int c) ? c : 0;
                    int row = int.TryParse(parts[2].Trim(), out int r) ? r : 0;
                    var locstr = parts[3].Trim();

                    if (WallLocation.TryParse(locstr, out var loc))
                    {
                        importedids.Add(id);
                        griditems.Add(new GridItem
                        {
                            Id = id,
                            Col = col,
                            Row = row,
                            LocationStr = locstr,
                            TargetLocation = loc,
                            Item = ext.Room?.Room?.WallItems.FirstOrDefault(x => x.Id == id)
                        });
                    }
                }
            }

                ignoreidchange = true;
            txtids.Text = string.Join("\n", importedids);
            ignoreidchange = false;
            lblitemcount.Content = $"Items: {griditems.Count}";

            if (griditems.Count > 0)
            {
                startlocation = griditems[0].TargetLocation;
                orientation = griditems[0].TargetLocation.Orientation;
            }

            setstatus($"loaded {griditems.Count} items");
            sendclientsidepreview();
        }
        catch (Exception ex)
        {
            setstatus($"load failed: {ex.Message}");
        }
    }

    private void onexport(object sender, RoutedEventArgs e)
    {
        if (griditems.Count == 0)
        {
            setstatus("nothing to save");
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Save Grid",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            DefaultExt = "txt"
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            var sb = new StringBuilder();
            foreach (var item in griditems)
                sb.AppendLine($"{item.Id}\t{item.Col}\t{item.Row}\t{item.LocationStr}");

            File.WriteAllText(dlg.FileName, sb.ToString());
            setstatus($"saved {griditems.Count} items");
        }
        catch (Exception ex)
        {
            setstatus($"save failed: {ex.Message}");
        }
    }

    private void sendclientsidepreview()
    {
        if (ext.Room?.Room == null) return;
        if (griditems.Count == 0) return;

        var extcopy = ext;
        var itemscopy = griditems.ToList();

        Task.Run(async () =>
        {
            foreach (var gi in itemscopy)
            {
                if (gi.Item == null) continue;
                try
                {
                    var updateditem = new WallItem(gi.Item) { Location = gi.TargetLocation };
                    var header = extcopy.Messages.Resolve(In.ItemUpdate);
                    var packet = new Xabbo.Messages.Packet(header, extcopy.Session.Client.Type);
                    packet.Write(updateditem);
                    extcopy.Send(packet);
                }
                catch { }
            }
        });
    }

    private void dopreview()
    {
        if (ext.Room?.Room == null)
        {
            setstatus("not in room");
            return;
        }

        if (startlocation == null)
        {
            setstatus("select group or import IDs first");
            return;
        }

        int.TryParse(txtcols.Text, out int columns);
        int.TryParse(txtrows.Text, out int rows);
        int.TryParse(txtspacingx.Text, out int spacingx);
        int.TryParse(txtspacingy.Text, out int spacingy);
        int.TryParse(txtrowoffset.Text, out int rowoffset);
        int.TryParse(txtzindex.Text, out int zindex);

        if (columns < 1) columns = 1;
        if (rows < 1) rows = 1;
        if (spacingx < 1) spacingx = 1;
        if (spacingy < 1) spacingy = 1;

        var room = ext.Room.Room;
        IWallItem[] itemstouse;

        if (importedids.Count > 0)
        {
            var roomitems = room.WallItems.ToDictionary(x => x.Id);
            itemstouse = importedids
                .Where(id => roomitems.ContainsKey(id))
                .Select(id => roomitems[id])
                .ToArray();

            if (itemstouse.Length == 0)
            {
                setstatus("no IDs found in room");
                return;
            }
        }
        else
        {
            setstatus("import furniture or add IDs");
            return;
        }

        var locations = GridPlacer.creategrid(room, startlocation.Value, columns, rows, spacingx, spacingy, rowoffset, zindex);

        griditems.Clear();
        int itemcount = Math.Min(itemstouse.Length, locations.Count);

        for (int i = 0; i < itemcount; i++)
        {
            int col = i % columns;
            int row = i / columns;

            griditems.Add(new GridItem
            {
                Id = itemstouse[i].Id,
                Col = col,
                Row = row,
                LocationStr = locations[i].ToString(),
                TargetLocation = locations[i],
                Item = itemstouse[i]
            });
        }

        lblitemcount.Content = $"Items: {itemcount}";

        var extcopy = ext;
        var itemscopy = griditems.ToList();

        Task.Run(async () =>
        {
            foreach (var gi in itemscopy)
            {
                try
                {
                    var updateditem = new WallItem(gi.Item!) { Location = gi.TargetLocation };
                    var header = extcopy.Messages.Resolve(In.ItemUpdate);
                    var packet = new Xabbo.Messages.Packet(header, extcopy.Session.Client.Type);
                    packet.Write(updateditem);
                    extcopy.Send(packet);
                }
                catch { }
            }

            Dispatcher.Invoke(() => setstatus($"preview: {itemscopy.Count}"));
        });
    }

    private void onmoveall(object sender, RoutedEventArgs e)
    {
        if (griditems.Count == 0)
        {
            setstatus("capture & import items first");
            return;
        }

        var itemstomove = griditems.ToList();
        var extcopy = ext;

        setstatus($"moving {itemstomove.Count}...");

        Task.Run(async () =>
        {
            int moved = 0;
            string lasterror = "";

            foreach (var gi in itemstomove)
            {
                try
                {
                    var locstr = gi.TargetLocation.ToString();
                    extcopy.Send(Out.MoveWallItem, gi.Item!.Id, locstr);
                    moved++;
                }
                catch (Exception ex)
                {
                    lasterror = ex.Message;
                }
                await Task.Delay(66);
            }

            Dispatcher.Invoke(() =>
            {
                if (moved == 0 && !string.IsNullOrEmpty(lasterror))
                    setstatus($"error: {lasterror}");
                else
                    setstatus($"moved {moved}");
            });
        });
    }

    private void setstatus(string text)
    {
        lblstatus.Content = text;
    }
}
