namespace Snoop;

using System;

[Flags]
public enum TargetRuntime
{
    None = 0,
    NetFramework = 1,
    CoreClr = 2
}
