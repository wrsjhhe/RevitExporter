using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Schema2;
using System.Numerics;
using Microsoft.Win32;
using Autodesk.Revit.DB.Visual;
using System.IO;

namespace RevitExporter.Exporter
{
    using VERTEX = VertexPosition;
    using RMaterial = Autodesk.Revit.DB.Material;
    class GLTFExpoterContext : IExportContext
    {
        public ModelRoot Model { get; private set; }

        private Scene _scene;
        private Dictionary<string, MaterialBuilder> _materials = new Dictionary<string, MaterialBuilder>();
        private MaterialBuilder _currentMaterial;
        private MeshBuilder<VERTEX, VertexTexture1> _currentMesh;

        Stack<Document> _documentStack = new Stack<Document>();
        Stack<Transform> _transformationStack = new Stack<Transform>();

        private int _precision;//转换精度
        private string _textureFolder;       //材质库地址
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="doc">导出的文档对象</param>
        /// <param name="precisionValue">转换精度</param>
        public GLTFExpoterContext(Document doc, int precisionValue)
        {
            _documentStack.Push(doc);
            _transformationStack.Push(Transform.Identity);//Transform.Identity 单位矩阵
            this._precision = precisionValue;
        }

        /// <summary>
        /// 当前正在导出的文档
        /// </summary>
        Document CurrentDocument
        {
            get
            {
                return _documentStack.Peek();
            }
        }

        /// <summary>
        /// 当前正在使用的模型转换矩阵
        /// </summary>
        Transform CurrentTransform
        {
            get
            {
                return _transformationStack.Peek();
            }
        }

        /// <summary>
        /// 导出开始
        /// </summary>
        /// <returns></returns>
        public bool Start()
        {
            _currentMaterial = new MaterialBuilder()
                .WithDoubleSide(true)
                .WithMetallicRoughnessShader()
                .WithChannelParam("BaseColor", new Vector4(0.5f, 0.5f, 0.5f, 1));
            _currentMaterial.UseChannel("MetallicRoughness");

            _materials.Add("Default", _currentMaterial);

            Model = ModelRoot.CreateModel();

            _scene = Model.UseScene("Default");

            //通过读取注册表相应键值获取材质库地址
            RegistryKey hklm = Registry.LocalMachine;
            RegistryKey libraryPath = hklm.OpenSubKey("SOFTWARE\\Wow6432Node\\Autodesk\\ADSKAdvancedTextureLibrary\\1");
            _textureFolder = libraryPath.GetValue("LibraryPaths").ToString() + "1\\Mats\\";
            hklm.Close();
            libraryPath.Close();

            //启动return ture
            return true;
        }

        /// <summary>
        /// This method marks the start of processing a view (a 3D view)
        /// </summary>
        public RenderNodeAction OnViewBegin(ViewNode node)
        {
            //导出3D视图 对视图没什么要处理的 直接： return RenderNodeAction.Proceed;
            /*0到15 默认8 级别越小减面的程度越高，最优是0最低是15总共份16级
          * SolidOrShellTessellationControls.LevelOfDetail曲面细分着色器控制lod范围0到1；
          * ViewNode.LevelOfDetail是视图将呈现的详细程度，取值范围[0,15]Revit将在细分面时使用建议的详细程度； 否则，它将使用基于输出分辨率的默认算法。\
          * 如果要求明确的细节级别（即正值），则使用接近有效范围中间值的值会产生非常合理的细分。 Revit使用级别8作为其“正常” LoD。
          * 对于face.Triangulate(precision) 详细程度。 其范围是从0到1。0是最低的详细级别，而1是最高的详细级别。
          */
            node.LevelOfDetail = _precision;
            return RenderNodeAction.Proceed;
        }

        /// <summary>
        /// 如果是链接模型，这里就是链接模型开始
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public RenderNodeAction OnLinkBegin(LinkNode node)
        {
            _documentStack.Push(node.GetDocument());
            _transformationStack.Push(CurrentTransform.Multiply(node.GetTransform()));
            return RenderNodeAction.Proceed;
        }

        /// <summary>
        /// 此方法标记要导出的图元的开始
        /// </summary>
        /// <param name="elementId"></param>
        /// <returns></returns>
        public RenderNodeAction OnElementBegin(ElementId elementId)
        {
            Element e = CurrentDocument.GetElement(elementId);
            if (e != null)
            {
                if (null == e.Category)
                {
                    return RenderNodeAction.Skip;
                }
            }

            //创建一个网格
            _currentMesh = new MeshBuilder<VERTEX, VertexTexture1>(elementId.IntegerValue.ToString());
            return RenderNodeAction.Proceed;
        }

        /// <summary>
        /// 此方法标记了要导出的实例的开始。
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public RenderNodeAction OnInstanceBegin(InstanceNode node)
        {
            _transformationStack.Push(CurrentTransform.Multiply(node.GetTransform()));
            return RenderNodeAction.Proceed;
        }

        /// <summary>
        /// 设置材质
        /// </summary>
        /// <remarks>
        //可以为每个单独的输出网格调用OnMaterial方法
        ///即使材质尚未实际更改。 因此通常
        ///有利于存储当前材料并仅获取其属性
        ///当材质实际更改时。
        /// </remarks>
        public void OnMaterial(MaterialNode node)
        {
            ElementId id = node.MaterialId;
            //是无效的objectID设置为默认的默认objectID
            if (ElementId.InvalidElementId != id)
            {
                Element m = CurrentDocument.GetElement
                    (node.MaterialId);
                SetCurrentMaterial(m.UniqueId, node);
            }
            else 
                SetDefaultMaterial();
        }

        public void OnLight(LightNode node)
        {
            //OnLight(LightNode node)方法，这个似乎是渲染时才有用，这里用不到，也留空。
        }
        /// <summary>
        /// 此方法标志着RPC对象导出的开始。
        /// </summary>
        /// <param name="node"></param>
        public void OnRPC(RPCNode node)
        {
            //再看OnRPC(RPCNode node)方法，API里写清楚了，这个方法只在使用IPhotoRenderContext时发挥作用
        }

        /// <summary>
        /// 导出face面
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public RenderNodeAction OnFaceBegin(FaceNode node)
        {
            return RenderNodeAction.Skip;
        }
        /// <summary>
        /// 结束face的导出
        /// </summary>
        /// <param name="node"></param>
        public void OnFaceEnd(FaceNode node)
        {
        }
        /// <summary>
        /// 输出Polymesh多边形网格，同时它也是属于face的
        /// </summary>
        /// <param name="node"></param>
        public void OnPolymesh(PolymeshTopology node)
        {
            //这里非常重要空间几何信息全都在这里
            int nPts = node.NumberOfPoints;
            int nFacets = node.NumberOfFacets;

            VertexBuilder<VertexPosition, VertexTexture1, VertexEmpty>[] vertexs = new VertexBuilder<VertexPosition, VertexTexture1, VertexEmpty>[nPts];
            XYZ p;
            UV uv;
            Transform t = CurrentTransform;

            for (int i = 0; i < nPts; i++)
            {
                p = t.OfPoint(node.GetPoint(i));
                uv = node.GetUV(i);
                vertexs[i] = new VertexBuilder<VertexPosition, VertexTexture1, VertexEmpty>(new VERTEX((float)(p.Y), (float)(p.Z), (float)(p.X)),
                    new VertexTexture1(new Vector2((float)uv.U, (float)uv.V)));

            }
           

            var mesh = new MeshBuilder<VertexPosition, VertexTexture1>();
            var prim = mesh.UsePrimitive(_currentMaterial);


            PolymeshFacet f;
            for (int i = 0; i < nFacets; i++)
            {
                f = node.GetFacet(i);
                prim.AddTriangle(vertexs[f.V1], vertexs[f.V2], vertexs[f.V3]);
            }
            _currentMesh.AddMesh(mesh, Matrix4x4.Identity);
        }
        /// <summary>
        /// 此方法标志着要导出的实例的结束。
        /// </summary>
        /// <param name="node"></param>
        public void OnInstanceEnd(InstanceNode node)
        {
            _transformationStack.Pop();
        }

        /// <summary>
        /// 导出图元结束
        /// </summary>
        /// <param name="elementId"></param>
        public void OnElementEnd(ElementId elementId)
        {
            Element e = CurrentDocument.GetElement(elementId);
            if (e != null)
            {
                if (null == e.Category)
                {
                    return;
                }
            }

            if (_currentMesh.Primitives.Count > 0)
            {
                var meshes = Model.CreateMeshes(_currentMesh);

                foreach (var mesh in meshes)
                {
                    _scene.CreateNode().WithMesh(mesh);
                }
            }
        }

        /// <summary>
        /// 如果是链接模型，这里就是链接模型结束
        /// </summary>
        /// <param name="node"></param>
        public void OnLinkEnd(LinkNode node)
        {
            _transformationStack.Pop();
            _documentStack.Pop();
        }

        /// <summary>
        /// This method marks the end of a 3D view being exported.
        /// </summary>
        /// <param name="elementId"></param>
        public void OnViewEnd(ElementId elementId)
        {
            
        }

        public bool IsCanceled()
        {
            return false;
        }

        /// <summary>
        /// 在程序处理完所有之后（或取消处理之后），在导出过程的最后将调用此方法。
        /// </summary>
        public void Finish()
        {
           
        }

        /*****************************************************************************************************************************************/
        /**********************************************************自定义方法*********************************************************************/
        /*****************************************************************************************************************************************/

        void SetDefaultMaterial()
        {
            _currentMaterial = _materials["Default"];
        }

        /// <summary>
        /// 设置当前材质
        /// </summary>
        /// <param name="uidMaterial"></param>
        void SetCurrentMaterial(string uidMaterial,MaterialNode node)
        {
            if (!_materials.ContainsKey(uidMaterial))
            {
                RMaterial material = CurrentDocument.GetElement(uidMaterial) as RMaterial;
                Color c = material.Color;
                MaterialBuilder m = null;
                try
                {
                    if (material.Transparency != 0)
                    {
                        m = new MaterialBuilder()
                       .WithAlpha(SharpGLTF.Materials.AlphaMode.BLEND)
                       .WithDoubleSide(true)
                       .WithMetallicRoughnessShader()
                       .WithChannelParam(KnownChannel.BaseColor, new Vector4(c.Red / 256f, c.Green / 256f, c.Blue / 256f, 1 - (material.Transparency / 128f)));
                    }
                    else
                    {
                        m = new MaterialBuilder()
                                           .WithDoubleSide(true)
                                           .WithMetallicRoughnessShader()
                                           .WithChannelParam(KnownChannel.BaseColor, new Vector4(c.Red / 256f, c.Green / 256f, c.Blue / 256f, 1));
                    }
                    Autodesk.Revit.DB.Visual.Asset currentAsset;
                    if (node.HasOverriddenAppearance)
                    {
                        currentAsset = node.GetAppearanceOverride();
                    }
                    else
                    {
                        currentAsset = node.GetAppearance();
                    }

                    try
                    {
                        //取得Asset中贴图信息
                        Autodesk.Revit.DB.Visual.Asset findAssert = FindTextureAsset(currentAsset as AssetProperty);
                        if(findAssert != null)
                        {
                            string textureFile = (findAssert["unifiedbitmap_Bitmap"] as AssetPropertyString).Value.Split('|')[0];
                            //用Asset中贴图信息和注册表里的材质库地址得到贴图文件所在位置
                            string textureName = textureFile.Replace("/", "\\");

                            string texturePath = Path.Combine(_textureFolder, textureName);

                            m.WithChannelImage(KnownChannel.BaseColor, texturePath);
                        }
                        
                    }
                    catch (Exception e)
                    {
                        
                    }
                }
                catch (Exception e)
                {

                }

                _materials.Add(uidMaterial, m);
            }
            _currentMaterial = _materials[uidMaterial];
        }

        /// <summary>
        /// 自定义方法，判断Asset是否包含贴图信息
        /// </summary>
        /// <param name="asset"></param>
        /// <returns></returns>
        private bool IsTextureAsset(Autodesk.Revit.DB.Visual.Asset asset)
        {
            AssetProperty assetProprty = GetAssetProprty(asset, "assettype");
            if (assetProprty != null && (assetProprty as AssetPropertyString).Value == "texture")
            {
                return true;
            }
            return GetAssetProprty(asset, "unifiedbitmap_Bitmap") != null;
        }


        /// <summary>
        /// 自定义方法，根据名字获取对应的AssetProprty
        /// </summary>
        /// <param name="asset"></param>
        /// <param name="propertyName"></param>
        /// <returns></returns>
        private AssetProperty GetAssetProprty(Autodesk.Revit.DB.Visual.Asset asset, string propertyName)
        {
            for (int i = 0; i < asset.Size; i++)
            {
                if (asset[i].Name == propertyName)
                {
                    return asset[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 自定义方法，用递归找到包含贴图信息：Asset包含的AssetProperty有多种类型，其中Asset、Properties
        /// 和Reference这三种必须递归处理。贴图信息的AssetProperty名字是unifiedbitmap_Bitmap，类型是String。
        /// </summary>
        /// <param name="ap"></param>
        /// <returns></returns>
        private Autodesk.Revit.DB.Visual.Asset FindTextureAsset(AssetProperty ap)
        {
            Autodesk.Revit.DB.Visual.Asset result = null;
            if (ap.Type == AssetPropertyType.Asset)
            {
                if (!IsTextureAsset(ap as Autodesk.Revit.DB.Visual.Asset))
                {
                    for (int i = 0; i < (ap as Autodesk.Revit.DB.Visual.Asset).Size; i++)
                    {
                        if (null != FindTextureAsset((ap as Autodesk.Revit.DB.Visual.Asset)[i]))
                        {
                            result = FindTextureAsset((ap as Autodesk.Revit.DB.Visual.Asset)[i]);
                            break;
                        }
                    }
                }
                else
                {
                    result = ap as Autodesk.Revit.DB.Visual.Asset;
                }
                return result;
            }
            else
            {
                for (int j = 0; j < ap.NumberOfConnectedProperties; j++)
                {
                    if (null != FindTextureAsset(ap.GetConnectedProperty(j)))
                    {
                        result = FindTextureAsset(ap.GetConnectedProperty(j));
                    }
                }
                return result;
            }
        }
    }
}
