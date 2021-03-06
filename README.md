# NCM
A .Net color conversion/managment library

## Status

- [x] Standard color conversion
- [x] Reading ICC profiles
- [x] Writing ICC profiles
- [x] XML comments documentation (not for ICC yet)
- [ ] ICC color conversion (partially implemented)
- [ ] Documentation (code and usage)

## Basic Usage

```csharp
var col1 = new ColorRGB(0.35, 0.17, 0.63, ColorspaceRGB.AdobeRGB);
var col2 = new ColorXYZ(Whitepoint.D50);

using (ColorConverter conv = new ColorConverter(col1, col2))
{
    conv.Convert();
        
    Console.WriteLine(col2.X);
    Console.WriteLine(col2.Y);
    Console.WriteLine(col2.Z);
}
```
If you want to change the input color, it's best to use it like this:

```csharp
var col1 = new ColorRGB(0.35, 0.17, 0.63, ColorspaceRGB.AdobeRGB);
var col2 = new ColorXYZ(Whitepoint.D50);

using (ColorConverter conv = new ColorConverter(col1, col2))
{
    conv.Convert();
    Console.WriteLine("First conversion:");
    Console.WriteLine(col2.X);
    Console.WriteLine(col2.Y);
    Console.WriteLine(col2.Z);

    Console.WriteLine("Changing values...");
    col1[0] = 0.53;
    col1[1] = 0.72;
    col1[2] = 0.24;
    conv.Convert();
        
    Console.WriteLine("Second conversion:");
    Console.WriteLine(col2.X);
    Console.WriteLine(col2.Y);
    Console.WriteLine(col2.Z);
}
```

In general, keep in mind that creating a ColorConverter is expensive while the Convert method is cheap.

## Using the Source Code

You need a C# coding environment with C#6 support. e.g. Visual Studio 2015 (any version is fine).
The building process is like any other C# project and has no external dependencies.

## Contributions

First off, thank you for any contribution!

If you found a bug, please report it and I will try to fix it as soon as possible.

I accept pull requests for bugfixes or new features but please first
 - check formatting and style
 - run the unit tests and see if they pass
 - add unit test(s) for the fix/feature (if possible)
