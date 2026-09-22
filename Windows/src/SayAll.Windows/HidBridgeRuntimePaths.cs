using System.IO;
using SayAll.HidBridge.Contracts;

namespace SayAll.Windows;

public static class HidBridgeRuntimePaths
{
    public static string RootDirectory => HidBridgeServiceContract.SharedRootDirectory;

    public static string SharedEventLogPath => HidBridgeServiceContract.SharedEventLogPath;
}
