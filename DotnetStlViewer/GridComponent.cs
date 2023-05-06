using Microsoft.Extensions.Logging;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System.Numerics;

public unsafe class GridComponent : Component
{
    private readonly ILogger<GridComponent> logger;
    private ComPtr<ID3D11VertexShader> vertexShader = default;
    private ComPtr<ID3D11PixelShader> pixelShader = default;
    private ComPtr<ID3D11InputLayout> inputLayout = default;
    private ComPtr<ID3D11Buffer> vertexBuffer = default;
    private ComPtr<ID3D11Buffer> vertexBuffer2 = default;
    private ComPtr<ID3D11Buffer> constantBuffer = default;
    private ModelViewProjectionConstantBuffer constantBufferData;
    private ComPtr<ID3D11DepthStencilState> depthStencilDefaultState = default;
    private ComPtr<ID3D11DepthStencilState> depthStencilDisabledState = default;
    private ComPtr<ID3D11RasterizerState> pRSsolidFrame = default;
    private uint gridEdgeVertexCount = 6;
    private uint gridVertexCount;
    private const int gridSize = 12;

    public GridComponent(ILogger<GridComponent> logger)
    {
        this.logger = logger;
    }

    public override void Initialize(IApp app)
    {
        ref var device = ref app.GraphicsContext.device.Get();
        var compilerApi = D3DCompiler.GetApi();

        // Create vertex shader
        var compileFlags = 0u;
#if DEBUG
        compileFlags |= (1 << 0) | (1 << 2);
#endif

        logger.LogInformation("CreateVertexShader");
        ID3D10Blob* vertexShaderBlob;
        ID3D10Blob* errorMsgs;
        fixed (char* fileName = Helpers.GetAssetFullPath(@"GridShader.hlsl"))
        {
            compilerApi.CompileFromFile(fileName
            , null
            , null
            , "VS"
            , "vs_4_0"
            , compileFlags
            , 0
            , &vertexShaderBlob
            , &errorMsgs)
            .ThrowHResult();
        }

        device.CreateVertexShader(
            vertexShaderBlob->GetBufferPointer()
            , vertexShaderBlob->GetBufferSize()
            , null
            , vertexShader.GetAddressOf())
            .ThrowHResult();

        if (errorMsgs != null)
            errorMsgs->Release();

        // Pixel shader
        logger.LogInformation("CreatePixelShader");
        ID3D10Blob* pixelShaderBlob;
        fixed (char* fileName = Helpers.GetAssetFullPath(@"GridShaderPS.hlsl"))
        {
            compilerApi.CompileFromFile(fileName
                , null
                , null
                , "PS"
                , "ps_4_0"
                , compileFlags
                , 0
                , &pixelShaderBlob
                , &errorMsgs)
            .ThrowHResult();
        }

        device.CreatePixelShader(
            pixelShaderBlob->GetBufferPointer()
            , pixelShaderBlob->GetBufferSize()
            , null
            , pixelShader.GetAddressOf())
        .ThrowHResult();

        pixelShaderBlob->Release();
        // Create input layout
        var lpPOSITION = (byte*)SilkMarshal.StringToPtr("POSITION", NativeStringEncoding.LPStr);
        var lpCOLOR = (byte*)SilkMarshal.StringToPtr("COLOR", NativeStringEncoding.LPStr);

        var inputLayouts = stackalloc InputElementDesc[]
        {
            new InputElementDesc
            {
                SemanticName = lpPOSITION,
                SemanticIndex = 0,
                Format = Format.FormatR32G32B32Float,
                InputSlot = 0,
                AlignedByteOffset = 0,
                InputSlotClass = InputClassification.PerVertexData,
                InstanceDataStepRate = 0
            },
            new InputElementDesc
            {
                SemanticName = lpCOLOR,
                SemanticIndex = 0,
                Format = Format.FormatR32G32B32A32Float,
                InputSlot = 0,
                AlignedByteOffset = 12,
                InputSlotClass = InputClassification.PerVertexData,
                InstanceDataStepRate = 0
            },
        };

        logger.LogInformation("CreateInputLayout");
        device.CreateInputLayout(
                inputLayouts
                , 2
                , vertexShaderBlob->GetBufferPointer()
                , vertexShaderBlob->GetBufferSize()
                , inputLayout.GetAddressOf())
            .ThrowHResult();

        SilkMarshal.Free((nint)lpPOSITION);
        SilkMarshal.Free((nint)lpCOLOR);

        // Create constantBuffer
        var cbufferDesc = new BufferDesc();
        cbufferDesc.Usage = Usage.Default;
        cbufferDesc.ByteWidth = (uint)Helpers.RoundUp(sizeof(ModelViewProjectionConstantBuffer), 16);
        cbufferDesc.BindFlags = (uint)BindFlag.ConstantBuffer;
        cbufferDesc.CPUAccessFlags = 0;

        device.CreateBuffer(cbufferDesc, null, constantBuffer.GetAddressOf())
            .ThrowHResult();

        var device2 = app.GraphicsContext.device;

        // Create Grid
        var quadSize = 2f;
        var grid = CreateGrid(gridSize, quadSize);
        gridVertexCount = (uint)grid.Length;

        CreateBuffer(ref device2, ref vertexBuffer, grid);

        var halfQuadSize = quadSize / 2;
        var v1 = new Vector3(-halfQuadSize, 0, -halfQuadSize);
        var v2 = new Vector3(halfQuadSize,  0, -halfQuadSize);
        var v3 = new Vector3(-halfQuadSize, 0, halfQuadSize);
        var v4 = new Vector3(halfQuadSize,  0, halfQuadSize);
        var cc = new Vector4(1);

        var quadVertices = new VertexPositionColor[]
        {
            new VertexPositionColor {  Position = v1, Color = new Vector4(1) },
            new VertexPositionColor {  Position = v2, Color = new Vector4(1) },
            new VertexPositionColor {  Position = v3, Color = new Vector4(1) },
            new VertexPositionColor {  Position = v3, Color = new Vector4(1) },
            new VertexPositionColor {  Position = v2, Color = new Vector4(1) },
            new VertexPositionColor {  Position = v4, Color = new Vector4(1) }
        };


        CreateBuffer(ref device2, ref vertexBuffer2, quadVertices);

        DepthStencilDesc depthStencilDisabledDesc;
        depthStencilDisabledDesc.DepthEnable = 0;
        depthStencilDisabledDesc.DepthWriteMask = DepthWriteMask.All;
        depthStencilDisabledDesc.DepthFunc = ComparisonFunc.Less;
        depthStencilDisabledDesc.StencilEnable = 1;
        depthStencilDisabledDesc.StencilReadMask = 0xFF;
        depthStencilDisabledDesc.StencilWriteMask = 0xFF;
        depthStencilDisabledDesc.FrontFace.StencilFailOp = StencilOp.Keep;
        depthStencilDisabledDesc.FrontFace.StencilDepthFailOp = StencilOp.Incr;
        depthStencilDisabledDesc.FrontFace.StencilPassOp = StencilOp.Keep;
        depthStencilDisabledDesc.FrontFace.StencilFunc = ComparisonFunc.Always;
        depthStencilDisabledDesc.BackFace.StencilFailOp = StencilOp.Keep;
        depthStencilDisabledDesc.BackFace.StencilDepthFailOp = StencilOp.Decr;
        depthStencilDisabledDesc.BackFace.StencilPassOp = StencilOp.Keep;
        depthStencilDisabledDesc.BackFace.StencilFunc = ComparisonFunc.Always;

        device.CreateDepthStencilState(depthStencilDisabledDesc, depthStencilDisabledState.GetAddressOf())
            .ThrowHResult();

        DepthStencilDesc depthStencilDefaultDesc;
        depthStencilDefaultDesc.DepthEnable = 1;
        depthStencilDefaultDesc.DepthWriteMask = DepthWriteMask.All;
        depthStencilDefaultDesc.DepthFunc = ComparisonFunc.Less;
        depthStencilDefaultDesc.StencilEnable = 1;
        depthStencilDefaultDesc.StencilReadMask = 0xFF;
        depthStencilDefaultDesc.StencilWriteMask = 0xFF;
        depthStencilDefaultDesc.FrontFace.StencilFailOp = StencilOp.Keep;
        depthStencilDefaultDesc.FrontFace.StencilDepthFailOp = StencilOp.Incr;
        depthStencilDefaultDesc.FrontFace.StencilPassOp = StencilOp.Keep;
        depthStencilDefaultDesc.FrontFace.StencilFunc = ComparisonFunc.Always;
        depthStencilDefaultDesc.BackFace.StencilFailOp = StencilOp.Keep;
        depthStencilDefaultDesc.BackFace.StencilDepthFailOp = StencilOp.Decr;
        depthStencilDefaultDesc.BackFace.StencilPassOp = StencilOp.Keep;
        depthStencilDefaultDesc.BackFace.StencilFunc = ComparisonFunc.Always;

        device.CreateDepthStencilState(depthStencilDefaultDesc, depthStencilDefaultState.GetAddressOf())
            .ThrowHResult();

        // Rasterizer
        RasterizerDesc RSSolidFrameDesc;
        RSSolidFrameDesc.FillMode = FillMode.Solid;
        RSSolidFrameDesc.CullMode = CullMode.Back;
        RSSolidFrameDesc.ScissorEnable = 0;
        RSSolidFrameDesc.DepthBias = 0;
        RSSolidFrameDesc.FrontCounterClockwise = 0;
        RSSolidFrameDesc.DepthBiasClamp = 0;
        RSSolidFrameDesc.SlopeScaledDepthBias = 0;
        RSSolidFrameDesc.DepthClipEnable = 1;
        RSSolidFrameDesc.MultisampleEnable = 0;
        RSSolidFrameDesc.AntialiasedLineEnable = 1;

        device.CreateRasterizerState(&RSSolidFrameDesc, pRSsolidFrame.GetAddressOf());
    }

    private void CreateBuffer(ref ComPtr<ID3D11Device> device, ref ComPtr<ID3D11Buffer> buffer, VertexPositionColor[] vertices)
    {
        // Vertex buffer
        var bufferDesc = new BufferDesc();
        bufferDesc.Usage = Usage.Default;
        bufferDesc.ByteWidth = (uint)sizeof(VertexPositionColor) * (uint)vertices.Length;
        bufferDesc.BindFlags = (uint)BindFlag.VertexBuffer;
        bufferDesc.CPUAccessFlags = 0;

        var subresourceData = new SubresourceData();
        fixed (VertexPositionColor* data = vertices)
        {
            subresourceData.PSysMem = data;
        }

        logger.LogInformation("CreateBuffer (Vertex buffer)");
        device.GetPinnableReference()
            ->CreateBuffer( bufferDesc,  subresourceData, buffer.GetAddressOf())
            .ThrowHResult();
    }

    private VertexPositionColor[] CreateGrid(int size, float mul = 1)
    {
        var vertexList = new VertexPositionColor[(size + 1) * 4];
        var m = (1f / size) * mul;

        float QuadDistance = (float)size / 2;
        var cc = new Vector4(0.8f);
        for (int i = 0; i < size + 1; i++)
        {
            float position = i - QuadDistance;
            vertexList[4 * i/**/] = new VertexPositionColor { Position = new Vector3(position * m, 0, QuadDistance * m), Color = cc };
            vertexList[4 * i + 1] = new VertexPositionColor { Position = new Vector3(position * m, 0, -QuadDistance * m), Color = cc };
            vertexList[4 * i + 2] = new VertexPositionColor { Position = new Vector3(QuadDistance * m, 0, position * m), Color = cc };
            vertexList[4 * i + 3] = new VertexPositionColor { Position = new Vector3(-QuadDistance * m, 0, position * m), Color = cc };
        }

        return vertexList;
    }

    public override void Draw(IApp app, ICamera camera, double time)
    {
        var deviceContext = app.GraphicsContext.deviceContext.GetPinnableReference();
        uint stride = (uint)sizeof(VertexPositionColor);
        uint offset = 0;

        var modelMatrix = camera.GetRotation();
        var viewMatrix = camera.GetView();
        var projectionMatrix = camera.GetProjection();

        constantBufferData.model = Matrix4x4.Transpose(modelMatrix);
        constantBufferData.view = Matrix4x4.Transpose(viewMatrix);
        constantBufferData.projection = Matrix4x4.Transpose(projectionMatrix);
        fixed (ModelViewProjectionConstantBuffer* data = &constantBufferData)
        {
            deviceContext->UpdateSubresource((ID3D11Resource*)constantBuffer.GetPinnableReference(), 0, null, data, 0, 0);
        }

        deviceContext->IASetInputLayout(inputLayout);
        deviceContext->VSSetShader(vertexShader, null, 0);
        deviceContext->PSSetShader(pixelShader, null, 0);

        deviceContext->VSSetConstantBuffers(0, 1, constantBuffer.GetAddressOf());

        deviceContext->OMSetDepthStencilState(depthStencilDisabledState.GetPinnableReference(), 1);

        deviceContext->RSSetState(pRSsolidFrame);

        deviceContext->IASetPrimitiveTopology(D3DPrimitiveTopology.D3D11PrimitiveTopologyTrianglelist);
        deviceContext->IASetVertexBuffers(0, 1, vertexBuffer2.GetAddressOf(), stride, offset);
        deviceContext->Draw(gridEdgeVertexCount, 0);

        deviceContext->IASetPrimitiveTopology(D3DPrimitiveTopology.D3D11PrimitiveTopologyLinelist);
        deviceContext->IASetVertexBuffers(0, 1, vertexBuffer.GetAddressOf(), stride, offset);
        deviceContext->Draw(gridVertexCount, 0);

        deviceContext->OMSetDepthStencilState(depthStencilDefaultState.GetPinnableReference(), 1);
    }
}
