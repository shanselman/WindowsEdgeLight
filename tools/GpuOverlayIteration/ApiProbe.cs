using System.Reflection;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;

internal static class ApiProbe
{
    public static void DumpAll()
    {
        Dump(typeof(ID2D1Factory1), "Create");
        Dump(typeof(ID2D1Device), "CreateDeviceContext");
        Dump(typeof(ID2D1DeviceContext), "CreateBitmap", "CreateBitmapFromDxgiSurface", "DrawBitmap", "PushLayer", "EndDraw", "SetDpi");
        Dump(typeof(ID2D1Geometry), "CombineWithGeometry");
        Dump(typeof(IDCompositionDevice), "CreateTargetForHwnd", "CreateVisual", "Commit");
        Dump(typeof(IDCompositionVisual), "SetContent", "SetOpacity");
        Dump(typeof(ID3D11Device), "CreateTexture2D");
        Dump(typeof(ID3D11DeviceContext), "Map", "CopyResource", "Unmap");
        Dump(typeof(IDXGIFactory2), "CreateSwapChainForComposition");
        Dump(typeof(IDXGISwapChain1), "Present", "GetBuffer");
        foreach (var t in new[] { typeof(BitmapProperties1), typeof(LayerParameters1), typeof(Texture2DDescription), typeof(SwapChainDescription1) })
        {
            Console.WriteLine(t.FullName);
            foreach (var c in t.GetConstructors()) Console.WriteLine(c);
            foreach (var f in t.GetFields()) Console.WriteLine(f);
        }
    }

    private static void Dump(Type type, params string[] prefixes)
    {
        foreach (var m in type.GetMethods().Where(m => prefixes.Any(p => m.Name.StartsWith(p, StringComparison.Ordinal))))
            Console.WriteLine(type.Name + ": " + m);
    }
}
