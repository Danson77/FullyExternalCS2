using CS2Cheat.Core.Data;
using CS2Cheat.Data.Entity;
using CS2Cheat.Data.Game;
using CS2Cheat.Features;
using CS2Cheat.Graphics;
using CS2Cheat.Utils;
using CS2Cheat.Utils.CFGManager;
using static CS2Cheat.Core.User32;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using Application = System.Windows.Application;

namespace CS2Cheat;

public class MainProgram:
    Application,
    IDisposable
{
    private MainProgram()
    {
        Offsets.UpdateOffsets();
        Startup += (_, _) => InitializeComponent();
        Exit += (_, _) => Dispose();
    }

    private GameProcess GameProcess { get; set; } = null!;

    private GameData GameData { get; set; } = null!;

    private WindowOverlay WindowOverlay { get; set; } = null!;

    private Graphics.Graphics Graphics { get; set; } = null!;
    private ModernGraphics ModernGfx { get; set; } = null!;
    private TriggerBot Trigger { get; set; } = null!;

    //private AimBot AimBot { get; set; } = null!;
    private AntiFlash AntiFlash { get; set; } = null!;
    private AutoAccept AutoAccept { get; set; } = null!;

    private BombTimer BombTimer { get; set; } = null!;
    private AutoDuck AutoDuck { get; set; } = null!;

    public void Dispose()
    {
        GameProcess.Dispose();
        GameProcess = default!;

        GameData.Dispose();
        GameData = default!;

        WindowOverlay.Dispose();
        WindowOverlay = default!;

        Graphics.Dispose();
        Graphics = default!;

        Trigger.Dispose();
        Trigger = default!;

        //AimBot.Dispose();
        //AimBot = default!;

        BombTimer.Dispose(); 
        BombTimer = default!;

        AutoDuck.Dispose();
        AutoDuck = default!;

        AntiFlash.Dispose();
        AntiFlash = default!;

        AutoAccept.Dispose();
        AutoAccept = default!;

        // in MainProgram.Dispose()
        AudioEngine.Dispose();

    }

    public static void Main()
    {
        new MainProgram().Run();
    }

    private void InitializeComponent()
    {
        // at the top of InitializeComponent()
        AudioEngine.Init();

        var features = ConfigManager.Load();
        GameProcess = new GameProcess();
        GameProcess.Start();

        GameData = new GameData(GameProcess);
        GameData.Start();

        WindowOverlay = new WindowOverlay(GameProcess);
        WindowOverlay.Start();

        Graphics = new Graphics.Graphics(GameProcess, GameData, WindowOverlay);
        Graphics.Start();

        ModernGfx = new ModernGraphics(GameProcess, GameData);
        ModernGfx.Start();

        Trigger = new TriggerBot(GameProcess, GameData);
        Trigger.Start();

        //AimBot = new AimBot(GameProcess, GameData);
        //AimBot.Start();

        BombTimer = new BombTimer(Graphics);
        BombTimer.Start();

        AutoDuck = new AutoDuck(GameProcess, GameData);
        AutoDuck.Start();

        AntiFlash = new AntiFlash(Graphics);
        AntiFlash.Start();

        AutoAccept = new AutoAccept(GameProcess, GameData);
        AutoAccept.Start();

        SetWindowDisplayAffinity(WindowOverlay!.Window.Handle, 0x00000011); //obs bypass
    }
}