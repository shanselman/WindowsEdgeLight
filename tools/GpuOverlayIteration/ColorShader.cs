using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Color = System.Windows.Media.Color;

internal sealed class ColorShader : IDisposable
{
    private readonly GpuDevice gpu;
    private readonly int width, height;
    private ID3D11Texture2D indexTexture = null!, output = null!;
    private ID3D11Buffer table = null!, constants = null!;
    private ID3D11ShaderResourceView indexView = null!, tableView = null!;
    private ID3D11RenderTargetView target = null!;
    private ID3D11VertexShader vertex = null!;
    private ID3D11PixelShader pixel = null!;
    private ID3D11RasterizerState rasterizer = null!;
    private uint lastColor = uint.MaxValue;
    public ID2D1Bitmap1 Bitmap { get; private set; } = null!;
    public int Requests { get; private set; }
    public int Updates { get; private set; }
    public long CacheUploadBytes { get; }
    public string CurrentColor => lastColor.ToString("X6");

    [StructLayout(LayoutKind.Sequential)]
    private struct Parameters { public uint R, G, B, Padding; }

    private const string Shader = """
        Texture2D<uint> pixelIds : register(t0);
        ByteAddressBuffer responses : register(t1);
        cbuffer ColorInput : register(b0) { uint3 color; uint padding; };
        float4 VS(uint id : SV_VertexID) : SV_Position
        {
            float2 uv = float2((id << 1) & 2, id & 2);
            return float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
        }
        uint ReadByte(uint index)
        {
            uint value = responses.Load(index & ~3u);
            return (value >> ((index & 3u) * 8u)) & 255u;
        }
        float4 PS(float4 position : SV_Position) : SV_Target
        {
            uint packed = pixelIds.Load(int3(int2(position.xy), 0));
            uint alpha = packed & 255u;
            if (alpha == 0) return 0;
            uint start = (packed >> 8u) * 80u;
            return float4(ReadByte(start + color.r - 180u),
                          ReadByte(start + color.g - 180u),
                          ReadByte(start + color.b - 180u), alpha) / 255.0;
        }
        """;

    public unsafe ColorShader(GpuDevice gpu, ID2D1DeviceContext drawing, ColorResponse response)
    {
        this.gpu = gpu;
        width = response.Width; height = response.Height;
        CacheUploadBytes = response.Indices.LongLength * 4 + response.Table.LongLength;
        try
        {
            var indexDescription = new Texture2DDescription
            {
                Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1,
                Format = Format.R32_UInt, SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Immutable, BindFlags = BindFlags.ShaderResource
            };
            fixed (uint* pointer = response.Indices)
                indexTexture = gpu.Device.CreateTexture2D(indexDescription,
                    [new SubresourceData((IntPtr)pointer, (uint)width * 4)]);
            indexView = gpu.Device.CreateShaderResourceView(indexTexture);
            table = gpu.Device.CreateBuffer(response.Table, BindFlags.ShaderResource, ResourceUsage.Immutable,
                CpuAccessFlags.None, ResourceOptionFlags.BufferAllowRawViews);
            tableView = gpu.Device.CreateShaderResourceView(table, new ShaderResourceViewDescription
            {
                Format = Format.R32_Typeless, ViewDimension = ShaderResourceViewDimension.BufferExtended,
                BufferEx = new BufferExtendedShaderResourceView
                {
                    FirstElement = 0, NumElements = (uint)response.Table.Length / 4,
                    Flags = BufferExtendedShaderResourceViewFlags.Raw
                }
            });
            constants = gpu.Device.CreateBuffer(16, BindFlags.ConstantBuffer);
            output = gpu.Device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource
            });
            target = gpu.Device.CreateRenderTargetView(output);
            using (var surface = output.QueryInterface<IDXGISurface>())
                Bitmap = drawing.CreateBitmapFromDxgiSurface(surface,
                    new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm,
                        Vortice.DCommon.AlphaMode.Premultiplied), 96, 96, BitmapOptions.None));
            vertex = gpu.Device.CreateVertexShader(Compiler.Compile(Shader, "VS", "ColorResponse", "vs_5_0").Span);
            pixel = gpu.Device.CreatePixelShader(Compiler.Compile(Shader, "PS", "ColorResponse", "ps_5_0").Span);
            rasterizer = gpu.Device.CreateRasterizerState(new RasterizerDescription
            {
                FillMode = Vortice.Direct3D11.FillMode.Solid, CullMode = CullMode.None, DepthClipEnable = true
            });
        }
        catch { Dispose(); throw; }
    }

    public void Update(Color color)
    {
        Requests++;
        if (color.R < 180 || color.G < 180 || color.B < 180)
            throw new InvalidOperationException("Unsupported production color range.");
        var key = ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
        if (lastColor == key) return;
        var parameters = new Parameters { R = color.R, G = color.G, B = color.B, Padding = 0 };
        var context = gpu.Immediate;
        context.UpdateSubresource(in parameters, constants);
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(vertex);
        context.PSSetShader(pixel);
        context.PSSetShaderResource(0, indexView);
        context.PSSetShaderResource(1, tableView);
        context.PSSetConstantBuffer(0, constants);
        context.RSSetState(rasterizer);
        context.RSSetViewport(0, 0, width, height);
        context.OMSetBlendState(null);
        context.OMSetDepthStencilState(null);
        context.OMSetRenderTargets(target, null);
        context.Draw(3, 0);
        context.ClearState();
        lastColor = key;
        Updates++;
    }

    public void Dispose()
    {
        Bitmap?.Dispose();
        rasterizer?.Dispose();
        vertex?.Dispose(); pixel?.Dispose();
        target?.Dispose(); output?.Dispose();
        indexView?.Dispose(); tableView?.Dispose();
        indexTexture?.Dispose(); table?.Dispose(); constants?.Dispose();
    }
}
