using System;
using System.Windows;

namespace LostAndDivine.Launcher;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new Application();
        app.Run(new MainWindow());
    }
}
