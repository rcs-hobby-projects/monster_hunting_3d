using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

var native = new NativeWindowSettings
{
    ClientSize = new Vector2i(1280, 720),
    Title = "Caçador de Monstros 3D",
    APIVersion = new Version(3, 3),
    Profile = ContextProfile.Core,
    NumberOfSamples = 4,
};

using var game = new MonsterHunt.Game(GameWindowSettings.Default, native);
game.Run();
