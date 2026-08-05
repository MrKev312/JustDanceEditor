using AssetsTools.NET;
using AssetsTools.NET.Extra;

using KevInc.Texture;
using KevInc.Texture.ImageSharp;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace JustDanceEditor.Formats.Unity.Bundles.Synthesis;

internal static class UnitySyntheticBundleFactory
{
    private const string UnityVersion = "2021.3.40f1";
    private const string SerializedFileUnityVersion = "0.0.0";
    private const uint DefaultTargetPlatform = 19;

    public static byte[] CreateEmptyBundle(uint targetPlatform = DefaultTargetPlatform)
    {
        AssetsFile assetsFile = CreateAssetsFile(targetPlatform);
        AssetBundleFile bundle = CreateBundle("CAB-00000000000000000000000000000000", assetsFile);
        return bundle.ToCompressedBundleData();
    }

    public static byte[] CreateImageBundle(
        string classDataPath,
        string bundleName,
        IReadOnlyList<UnityImageBundleAsset> assets,
        uint targetPlatform = DefaultTargetPlatform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(classDataPath);
        if (!File.Exists(classDataPath))
            throw new FileNotFoundException("Unity class database package was not found.", classDataPath);

        using FileStream stream = File.OpenRead(classDataPath);
        return CreateImageBundle(stream, bundleName, assets, targetPlatform);
    }

    public static byte[] CreateImageBundle(
        Stream classDataPackage,
        string bundleName,
        IReadOnlyList<UnityImageBundleAsset> assets,
        uint targetPlatform = DefaultTargetPlatform)
    {
        ArgumentNullException.ThrowIfNull(classDataPackage);
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleName);
        ArgumentNullException.ThrowIfNull(assets);

        if (assets.Count == 0)
            throw new ArgumentException("At least one image asset is required.", nameof(assets));

        AssetsManager manager = new();
        try
        {
            manager.LoadClassPackage(classDataPackage);
            ClassDatabaseFile classDatabase = manager.LoadClassDatabaseFromPackage(UnityVersion);
            AssetsFile assetsFile = CreateAssetsFile(targetPlatform);

            AddTypeTree(assetsFile, classDatabase, (int)AssetClassID.AssetBundle);
            AddTypeTree(assetsFile, classDatabase, (int)AssetClassID.Texture2D);
            AddTypeTree(assetsFile, classDatabase, (int)AssetClassID.Sprite);

            AssetTypeValueField assetBundleBase = CreateDefaultField(classDatabase, (int)AssetClassID.AssetBundle);
            assetBundleBase["m_Name"].AsString = bundleName;
            assetBundleBase["m_AssetBundleName"].AsString = bundleName;
            SetUInt(assetBundleBase["m_RuntimeCompatibility"], 1);
            SetInt(assetBundleBase["m_ExplicitDataLayout"], 1);

            long[] textureIds = new long[assets.Count];
            long[] spriteIds = new long[assets.Count];

            for (int i = 0; i < assets.Count; i++)
            {
                long textureId = 2 + (i * 2);
                long spriteId = textureId + 1;

                AssetTypeValueField textureBase = CreateDefaultField(classDatabase, (int)AssetClassID.Texture2D);
                AssetTypeValueField spriteBase = CreateDefaultField(classDatabase, (int)AssetClassID.Sprite);
                UpdateTexture(textureBase, assets[i]);
                UpdateSprite(spriteBase, assets[i], textureId);

                AddAsset(assetsFile, classDatabase, textureId, (int)AssetClassID.Texture2D, textureBase);
                AddAsset(assetsFile, classDatabase, spriteId, (int)AssetClassID.Sprite, spriteBase);

                textureIds[i] = textureId;
                spriteIds[i] = spriteId;
            }

            PopulateAssetBundle(assetBundleBase, assets, textureIds, spriteIds);
            AddAsset(assetsFile, classDatabase, 1, (int)AssetClassID.AssetBundle, assetBundleBase);

            AssetBundleFile bundle = CreateBundle("CAB-00000000000000000000000000000000", assetsFile);
            return bundle.ToCompressedBundleData();
        }
        finally
        {
            manager.UnloadAll(true);
        }
    }

    public static byte[] CreateMapPackageSeed(
        string classDataPath,
        uint targetPlatform = 38)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(classDataPath);
        if (!File.Exists(classDataPath))
            throw new FileNotFoundException("Unity class database package was not found.", classDataPath);

        using FileStream stream = File.OpenRead(classDataPath);
        return CreateMapPackageSeed(stream, targetPlatform);
    }

    public static byte[] CreateMapPackageSeed(
        Stream classDataPackage,
        uint targetPlatform = 38)
    {
        ArgumentNullException.ThrowIfNull(classDataPackage);

        AssetsManager manager = new();
        try
        {
            manager.LoadClassPackage(classDataPackage);
            ClassDatabaseFile classDatabase = manager.LoadClassDatabaseFromPackage(UnityVersion);
            AssetsFile assetsFile = CreateAssetsFile(targetPlatform);

            AddTypeTree(assetsFile, classDatabase, (int)AssetClassID.AssetBundle);
            AddTypeTree(assetsFile, classDatabase, (int)AssetClassID.MonoScript);
            AddTypeTree(assetsFile, classDatabase, (int)AssetClassID.TextAsset);
            AddTypeTree(assetsFile, classDatabase, (int)AssetClassID.Texture2D);
            AddTypeTree(assetsFile, classDatabase, (int)AssetClassID.Sprite);
            AddTypeTree(assetsFile, classDatabase, (int)AssetClassID.SpriteAtlas);

            TypeTreeType musicTrackType = UnityMapPackageTypeTreeFactory.CreateMusicTrackTypeTree();
            TypeTreeType mapType = UnityMapPackageTypeTreeFactory.CreateMapTypeTree();
            assetsFile.Metadata.TypeTreeTypes.Add(musicTrackType);
            assetsFile.Metadata.TypeTreeTypes.Add(mapType);

            const long assetBundleId = 1;
            const long musicScriptId = 10;
            const long mapScriptId = 11;
            const long musicTrackId = 20;
            const long mapId = 21;
            const long spriteAtlasId = 30;
            const long spriteTemplateId = 31;

            assetsFile.Metadata.ScriptTypes.Add(new AssetPPtr(0, musicScriptId));
            assetsFile.Metadata.ScriptTypes.Add(new AssetPPtr(0, mapScriptId));

            AssetTypeValueField musicScript = CreateDefaultField(classDatabase, (int)AssetClassID.MonoScript);
            PopulateMonoScript(musicScript, "MusicTrack", "MusicEngine", "com.ubisoft.justdance.conductor.dll");
            AddAsset(assetsFile, classDatabase, musicScriptId, (int)AssetClassID.MonoScript, musicScript);

            AssetTypeValueField mapScript = CreateDefaultField(classDatabase, (int)AssetClassID.MonoScript);
            PopulateMonoScript(mapScript, "JDMap", "JD.MapClasses", "com.ubisoft.justdance.mapclasses.dll");
            AddAsset(assetsFile, classDatabase, mapScriptId, (int)AssetClassID.MonoScript, mapScript);

            AssetTypeValueField musicTrack = CreateDefaultField(musicTrackType);
            musicTrack["m_Name"].AsString = string.Empty;
            SetPPtr(musicTrack["m_Script"], musicScriptId);
            AddMonoBehaviourAsset(assetsFile, musicTrackId, scriptIndex: 0, musicTrack);

            AssetTypeValueField map = CreateDefaultField(mapType);
            map["m_Name"].AsString = "Synthetic";
            map["MapName"].AsString = "Synthetic";
            map["SongDesc"]["MapName"].AsString = "Synthetic";
            map["KaraokeData"]["MapName"].AsString = "Synthetic";
            map["DanceData"]["MapName"].AsString = "Synthetic";
            SetPPtr(map["m_Script"], mapScriptId);
            SetPPtr(map["TrackData"], musicTrackId);
            SetPPtr(map["PictogramAtlas"], spriteAtlasId);
            AddMonoBehaviourAsset(assetsFile, mapId, scriptIndex: 1, map);

            AssetTypeValueField spriteAtlas = CreateDefaultField(classDatabase, (int)AssetClassID.SpriteAtlas);
            spriteAtlas["m_Name"].AsString = "Synthetic";
            spriteAtlas["m_Tag"].AsString = "Synthetic";
            AddAsset(assetsFile, classDatabase, spriteAtlasId, (int)AssetClassID.SpriteAtlas, spriteAtlas);

            AssetTypeValueField spriteTemplate = CreateDefaultField(classDatabase, (int)AssetClassID.Sprite);
            spriteTemplate["m_Name"].AsString = "synthetic_sprite_template";
            EnsureArrayElement(spriteTemplate["m_AtlasTags"]["Array"]).AsString = "Synthetic";
            EnsureArrayElement(spriteTemplate["m_RD"]["m_SubMeshes"]["Array"]);
            AddAsset(assetsFile, classDatabase, spriteTemplateId, (int)AssetClassID.Sprite, spriteTemplate);

            AssetTypeValueField assetBundle = CreateDefaultField(classDatabase, (int)AssetClassID.AssetBundle);
            assetBundle["m_Name"].AsString = "Synthetic_MapPackage";
            assetBundle["m_AssetBundleName"].AsString = "Synthetic_MapPackage";
            SetUInt(assetBundle["m_RuntimeCompatibility"], 1);
            SetInt(assetBundle["m_ExplicitDataLayout"], 1);
            AssetTypeValueField preloadArray = assetBundle["m_PreloadTable"]["Array"];
            AddPreload(preloadArray, musicScriptId);
            AddPreload(preloadArray, mapScriptId);
            AddPreload(preloadArray, musicTrackId);
            AddPreload(preloadArray, mapId);
            AddPreload(preloadArray, spriteAtlasId);
            AddPreload(preloadArray, spriteTemplateId);

            AssetTypeValueField containerArray = assetBundle["m_Container"]["Array"];
            AddContainer(containerArray, "MapPackage", 0, mapId);
            AddContainer(containerArray, "MapPackage", 0, musicTrackId);
            AddAsset(assetsFile, classDatabase, assetBundleId, (int)AssetClassID.AssetBundle, assetBundle);

            AssetBundleFile bundle = CreateBundle("CAB-00000000000000000000000000000000", assetsFile);
            return bundle.ToCompressedBundleData();
        }
        finally
        {
            manager.UnloadAll(true);
        }
    }

    private static AssetsFile CreateAssetsFile(uint targetPlatform)
    {
        return new AssetsFile
        {
            Header = new AssetsFileHeader
            {
                Version = 22,
                Endianness = false
            },
            Metadata = new AssetsFileMetadata
            {
                UnityVersion = SerializedFileUnityVersion,
                TargetPlatform = targetPlatform,
                TypeTreeEnabled = true,
                TypeTreeTypes = [],
                AssetInfos = [],
                ScriptTypes = [],
                Externals = [],
                RefTypes = [],
                UserInformation = string.Empty
            }
        };
    }

    private static AssetBundleFile CreateBundle(string cabName, AssetsFile assetsFile)
    {
        AssetBundleFile bundle = new()
        {
            Header = new AssetBundleHeader
            {
                Signature = "UnityFS",
                Version = 8,
                GenerationVersion = "5.x.x",
                EngineVersion = UnityVersion,
                FileStreamHeader = new AssetBundleFSHeader()
            },
            BlockAndDirInfo = new AssetBundleBlockAndDirInfo
            {
                Hash = Hash128.NewBlankHash(),
                BlockInfos = [],
                DirectoryInfos = []
            }
        };

        AssetBundleDirectoryInfo directoryInfo = AssetBundleDirectoryInfo.Create(cabName, true);
        directoryInfo.SetNewData(assetsFile);
        bundle.BlockAndDirInfo.DirectoryInfos.Add(directoryInfo);
        return bundle;
    }

    private static void AddTypeTree(AssetsFile assetsFile, ClassDatabaseFile classDatabase, int typeId)
    {
        assetsFile.Metadata.TypeTreeTypes.Add(ClassDatabaseTypeTreeConverter.Convert(classDatabase, typeId, preferEditor: false));
    }

    private static AssetTypeValueField CreateDefaultField(ClassDatabaseFile classDatabase, int typeId)
    {
        ClassDatabaseType classType = classDatabase.FindAssetClassByID(typeId)
                                      ?? throw new InvalidOperationException($"Unity class database does not contain type id {typeId}.");
        AssetTypeTemplateField template = new();
        template.FromClassDatabase(classDatabase, classType, preferEditor: false);
        return ValueBuilder.DefaultValueFieldFromTemplate(template);
    }

    private static AssetTypeValueField CreateDefaultField(TypeTreeType typeTree)
    {
        AssetTypeTemplateField template = new();
        template.FromTypeTree(typeTree);
        return ValueBuilder.DefaultValueFieldFromTemplate(template);
    }

    private static void AddAsset(AssetsFile assetsFile, ClassDatabaseFile classDatabase, long pathId, int typeId, AssetTypeValueField field)
    {
        AssetFileInfo info = AssetFileInfo.Create(assetsFile, pathId, typeId, classDatabase, preferEditor: false)
                             ?? throw new InvalidOperationException($"Could not create asset info for type id {typeId}.");
        info.SetNewData(field);
        assetsFile.Metadata.AddAssetInfo(info);
    }

    private static void AddMonoBehaviourAsset(AssetsFile assetsFile, long pathId, ushort scriptIndex, AssetTypeValueField field)
    {
        AssetFileInfo info = AssetFileInfo.Create(assetsFile, pathId, (int)AssetClassID.MonoBehaviour, scriptIndex, null, preferEditor: false)
                             ?? throw new InvalidOperationException($"Could not create MonoBehaviour asset info for script index {scriptIndex}.");
        info.SetNewData(field);
        assetsFile.Metadata.AddAssetInfo(info);
    }

    private static void PopulateMonoScript(AssetTypeValueField script, string className, string @namespace, string assemblyName)
    {
        script["m_Name"].AsString = className;
        script["m_ClassName"].AsString = className;
        script["m_Namespace"].AsString = @namespace;
        script["m_AssemblyName"].AsString = assemblyName;
    }

    private static AssetTypeValueField EnsureArrayElement(AssetTypeValueField arrayField)
    {
        if (arrayField.Children.Count == 0)
            arrayField.Children.Add(ValueBuilder.DefaultValueFieldFromArrayTemplate(arrayField));

        return arrayField.Children[0];
    }

    private static void UpdateTexture(AssetTypeValueField textureBase, UnityImageBundleAsset asset)
    {
        using Image<Rgba32> image = asset.Image.CloneAs<Rgba32>();
        image.Mutate(ctx => ctx.Resize(asset.Width, asset.Height).Flip(FlipMode.Vertical));
        byte[] encoded = TextureImageSharpCodec.EncodeData(image, asset.TextureFormat, quality: 5, mipCount: 1);

        textureBase["m_Name"].AsString = asset.TextureName;
        SetInt(textureBase["m_Width"], asset.Width);
        SetInt(textureBase["m_Height"], asset.Height);
        SetInt(textureBase["m_MipCount"], 1);
        SetInt(textureBase["m_TextureFormat"], (int)asset.TextureFormat);
        SetInt(textureBase["m_ForcedFallbackFormat"], (int)TextureFormat.RGBA32);
        SetInt(textureBase["m_ImageCount"], 1);
        SetInt(textureBase["m_TextureDimension"], 2);
        SetInt(textureBase["m_LightmapFormat"], 6);
        SetInt(textureBase["m_ColorSpace"], 1);
        SetBool(textureBase["m_IsAlphaChannelOptional"], asset.TextureFormat == TextureFormat.DXT1Crunched);
        SetUInt(textureBase["m_CompleteImageSize"], (uint)encoded.Length);
        SetByteArray(textureBase["image data"], encoded);

        SetInteger(textureBase["m_StreamData"]["offset"], 0);
        SetInteger(textureBase["m_StreamData"]["size"], 0);
        SetString(textureBase["m_StreamData"]["path"], string.Empty);

        SetInt(textureBase["m_TextureSettings"]["m_FilterMode"], 1);
        SetInt(textureBase["m_TextureSettings"]["m_Aniso"], 1);
        SetInt(textureBase["m_TextureSettings"]["m_WrapU"], 1);
        SetInt(textureBase["m_TextureSettings"]["m_WrapV"], 1);
        SetInt(textureBase["m_TextureSettings"]["m_WrapW"], 1);
    }

    private static void UpdateSprite(AssetTypeValueField spriteBase, UnityImageBundleAsset asset, long textureId)
    {
        spriteBase["m_Name"].AsString = asset.SpriteName;
        SetUInt(spriteBase["m_Extrude"], 1);
        SetRect(spriteBase["m_Rect"], 0, 0, asset.Width, asset.Height);
        SetVector2(spriteBase["m_Offset"], 0, 0);
        SetVector2(spriteBase["m_Pivot"], 0.5f, 0.5f);
        SetFloat(spriteBase["m_PixelsToUnits"], 100);

        AssetTypeValueField renderData = spriteBase["m_RD"];
        SetPPtr(renderData["texture"], textureId);
        SetRect(renderData["textureRect"], 0, 0, asset.Width, asset.Height);
        SetVector2(renderData["textureRectOffset"], 0, 0);
        SetVector2(renderData["atlasRectOffset"], -1, -1);
        SetUInt(renderData["settingsRaw"], 64);
        SetFloat(renderData["downscaleMultiplier"], 1);
        SetVector4(renderData["uvTransform"], 100, asset.Width / 2f, 100, asset.Height / 2f);
        PopulateSpriteQuad(spriteBase, asset.Width / 200f, asset.Height / 200f);

        AssetTypeValueField atlasTags = spriteBase["m_AtlasTags"]["Array"];
        if (!atlasTags.IsDummy)
            atlasTags.Children.Clear();

        uint[] renderKey = Guid.NewGuid().ToUnity();
        AssetTypeValueField key = spriteBase["m_RenderDataKey"]["first"];
        SetUInt(key["data[0]"], renderKey[0]);
        SetUInt(key["data[1]"], renderKey[1]);
        SetUInt(key["data[2]"], renderKey[2]);
        SetUInt(key["data[3]"], renderKey[3]);
        SetInteger(spriteBase["m_RenderDataKey"]["second"], 21300000);
    }

    private static void PopulateSpriteQuad(AssetTypeValueField spriteBase, float halfWidth, float halfHeight)
    {
        AssetTypeValueField renderData = spriteBase["m_RD"];
        AssetTypeValueField subMeshes = renderData["m_SubMeshes"]["Array"];
        subMeshes.Children.Clear();
        AssetTypeValueField subMesh = ValueBuilder.DefaultValueFieldFromArrayTemplate(subMeshes);
        SetUInt(subMesh["firstByte"], 0);
        SetUInt(subMesh["indexCount"], 6);
        SetInt(subMesh["topology"], 0);
        SetUInt(subMesh["baseVertex"], 0);
        SetUInt(subMesh["firstVertex"], 0);
        SetUInt(subMesh["vertexCount"], 4);
        SetVector3(subMesh["localAABB"]["m_Center"], 0, 0, 0);
        SetVector3(subMesh["localAABB"]["m_Extent"], 0, 0, 0);
        subMeshes.Children.Add(subMesh);

        SetByteArray(renderData["m_IndexBuffer"]["Array"], [3, 0, 0, 0, 1, 0, 2, 0, 1, 0, 0, 0]);
        SetUInt(renderData["m_VertexData"]["m_VertexCount"], 4);
        PopulateSpriteVertexChannels(renderData["m_VertexData"]["m_Channels"]["Array"]);
        SetByteArray(renderData["m_VertexData"]["m_DataSize"], CreateSpriteVertexData(halfWidth, halfHeight));

        AssetTypeValueField physicsShapes = spriteBase["m_PhysicsShape"]["Array"];
        physicsShapes.Children.Clear();
        AssetTypeValueField shape = ValueBuilder.DefaultValueFieldFromArrayTemplate(physicsShapes);
        AssetTypeValueField points = shape["Array"];
        points.Children.Clear();
        AddPhysicsPoint(points, -halfWidth, halfHeight);
        AddPhysicsPoint(points, -halfWidth, -halfHeight);
        AddPhysicsPoint(points, halfWidth, -halfHeight);
        AddPhysicsPoint(points, halfWidth, halfHeight);
        physicsShapes.Children.Add(shape);
    }

    private static void PopulateSpriteVertexChannels(AssetTypeValueField channels)
    {
        channels.Children.Clear();
        for (int i = 0; i < 14; i++)
        {
            AssetTypeValueField channel = ValueBuilder.DefaultValueFieldFromArrayTemplate(channels);
            SetByte(channel["stream"], (byte)(i == 4 ? 1 : 0));
            SetByte(channel["offset"], 0);
            SetByte(channel["format"], 0);
            SetByte(channel["dimension"], (byte)(i == 0 ? 3 : i == 4 ? 2 : 0));
            channels.Children.Add(channel);
        }
    }

    private static byte[] CreateSpriteVertexData(float halfWidth, float halfHeight)
    {
        byte[] data = new byte[80];
        WriteFloat(data, 0, -halfWidth);
        WriteFloat(data, 4, halfHeight);
        WriteFloat(data, 8, 0);
        WriteFloat(data, 12, halfWidth);
        WriteFloat(data, 16, -halfHeight);
        WriteFloat(data, 20, 0);
        WriteFloat(data, 24, halfWidth);
        WriteFloat(data, 28, halfHeight);
        WriteFloat(data, 32, 0);
        WriteFloat(data, 36, -halfWidth);
        WriteFloat(data, 40, -halfHeight);
        WriteFloat(data, 44, 0);
        return data;
    }

    private static void WriteFloat(byte[] data, int offset, float value)
    {
        BitConverter.TryWriteBytes(data.AsSpan(offset, sizeof(float)), value);
    }

    private static void AddPhysicsPoint(AssetTypeValueField points, float x, float y)
    {
        AssetTypeValueField point = ValueBuilder.DefaultValueFieldFromArrayTemplate(points);
        SetVector2(point, x, y);
        points.Children.Add(point);
    }

    private static void PopulateAssetBundle(AssetTypeValueField assetBundleBase, IReadOnlyList<UnityImageBundleAsset> assets, long[] textureIds, long[] spriteIds)
    {
        AssetTypeValueField preloadArray = assetBundleBase["m_PreloadTable"]["Array"];
        AssetTypeValueField containerArray = assetBundleBase["m_Container"]["Array"];
        preloadArray.Children.Clear();
        containerArray.Children.Clear();

        for (int i = 0; i < assets.Count; i++)
        {
            AddPreload(preloadArray, textureIds[i]);
            AddPreload(preloadArray, spriteIds[i]);

            AddContainer(containerArray, assets[i].ContainerName, i * 2, textureIds[i]);
            AddContainer(containerArray, assets[i].ContainerName, i * 2, spriteIds[i]);
        }
    }

    private static void AddPreload(AssetTypeValueField preloadArray, long pathId)
    {
        AssetTypeValueField entry = ValueBuilder.DefaultValueFieldFromArrayTemplate(preloadArray);
        SetPPtr(entry, pathId);
        preloadArray.Children.Add(entry);
    }

    private static void AddContainer(AssetTypeValueField containerArray, string containerName, int preloadIndex, long pathId)
    {
        AssetTypeValueField entry = ValueBuilder.DefaultValueFieldFromArrayTemplate(containerArray);
        entry["first"].AsString = containerName;
        SetInt(entry["second"]["preloadIndex"], preloadIndex);
        SetInt(entry["second"]["preloadSize"], 2);
        SetPPtr(entry["second"]["asset"], pathId);
        containerArray.Children.Add(entry);
    }

    private static void SetPPtr(AssetTypeValueField field, long pathId)
    {
        if (field.IsDummy)
            return;

        SetInt(field["m_FileID"], 0);
        if (!field["m_PathID"].IsDummy)
            field["m_PathID"].AsLong = pathId;
        else
            SetInteger(field, pathId);
    }

    private static void SetRect(AssetTypeValueField field, float x, float y, float width, float height)
    {
        if (field.IsDummy)
            return;

        SetFloat(field["x"], x);
        SetFloat(field["y"], y);
        SetFloat(field["width"], width);
        SetFloat(field["height"], height);
    }

    private static void SetVector2(AssetTypeValueField field, float x, float y)
    {
        if (field.IsDummy)
            return;

        SetFloat(field["x"], x);
        SetFloat(field["y"], y);
    }

    private static void SetVector3(AssetTypeValueField field, float x, float y, float z)
    {
        if (field.IsDummy)
            return;

        SetFloat(field["x"], x);
        SetFloat(field["y"], y);
        SetFloat(field["z"], z);
    }

    private static void SetVector4(AssetTypeValueField field, float x, float y, float z, float w)
    {
        if (field.IsDummy)
            return;

        SetFloat(field["x"], x);
        SetFloat(field["y"], y);
        SetFloat(field["z"], z);
        SetFloat(field["w"], w);
    }

    private static void SetInt(AssetTypeValueField field, int value)
    {
        if (!field.IsDummy)
            field.AsInt = value;
    }

    private static void SetByte(AssetTypeValueField field, byte value)
    {
        if (!field.IsDummy)
            field.AsByte = value;
    }

    private static void SetUInt(AssetTypeValueField field, uint value)
    {
        if (!field.IsDummy)
            field.AsUInt = value;
    }

    private static void SetBool(AssetTypeValueField field, bool value)
    {
        if (!field.IsDummy)
            field.AsBool = value;
    }

    private static void SetInteger(AssetTypeValueField field, long value)
    {
        if (field.IsDummy)
            return;

        switch (field.Value.ValueType)
        {
            case AssetValueType.UInt64:
                field.AsULong = unchecked((ulong)value);
                break;
            case AssetValueType.UInt32:
                field.AsUInt = unchecked((uint)value);
                break;
            case AssetValueType.Int32:
                field.AsInt = unchecked((int)value);
                break;
            default:
                field.AsLong = value;
                break;
        }
    }

    private static void SetFloat(AssetTypeValueField field, float value)
    {
        if (!field.IsDummy)
            field.AsFloat = value;
    }

    private static void SetString(AssetTypeValueField field, string value)
    {
        if (!field.IsDummy)
            field.AsString = value;
    }

    private static void SetByteArray(AssetTypeValueField field, byte[] value)
    {
        if (!field.IsDummy)
            field.AsByteArray = value;
    }
}
