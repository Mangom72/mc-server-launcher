using System;
using System.Threading.Tasks;

class Program
{
    static void Main()
    {
        Console.WriteLine("Testing UPnP...");
        var t = Launcher.UpnpCore.DiscoverAndMapPortAsync(25565, 25565, "Minecraft Server Test");
        t.Wait();
        Console.WriteLine("Done.");
    }
}