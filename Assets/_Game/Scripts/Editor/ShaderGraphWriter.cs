using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace CoopDeck.EditorTools
{
    /// <summary>
    /// Writes a URP Unlit Shader Graph (.shadergraph, the editor's multi-JSON format) whose work happens in
    /// Custom Function nodes (File mode) that read the graph's properties by reference name - the same
    /// structure as SG_Character_SoftToon. Built-in input nodes (UV0, vertex colour, positions, normals,
    /// view direction) feed the functions; their outputs drive Base Color / Alpha and, optionally, the vertex
    /// Position. Object ids are hashed from the graph name, so regenerating an unchanged graph writes the
    /// same file (no churn in version control).
    /// </summary>
    public sealed class ShaderGraphWriter
    {
        public enum Input
        {
            UV0,
            VertexColor,
            PositionOS,
            PositionWS,
            NormalOS,
            NormalWS,
            ViewDirWS,
        }

        public enum Blend
        {
            Alpha = 0,
            Additive = 2,
        }

        public enum Faces
        {
            Both = 0,
            Front = 2,
        }

        readonly string graphName;
        readonly List<string> objects = new List<string>();
        readonly List<string> propertyIds = new List<string>();
        readonly List<string> nodeIds = new List<string>();
        readonly List<string> edges = new List<string>();
        readonly List<string> vertexBlocks = new List<string>();
        readonly List<string> fragmentBlocks = new List<string>();
        readonly Dictionary<Input, string> inputNodes = new Dictionary<Input, string>();

        public string MenuPath = "CoopDeck/FX";
        public Blend BlendMode = Blend.Additive;
        public Faces RenderFace = Faces.Front;

        public ShaderGraphWriter(string graphName)
        {
            this.graphName = graphName;
        }

        // ------------------------------------------------------------------ properties

        public void FloatProperty(string displayName, string reference, float value, float min = 0f, float max = 1f, bool slider = true)
        {
            string id = Id("prop/" + reference);
            propertyIds.Add(id);
            objects.Add($@"{{
    ""m_SGVersion"": 1,
    ""m_Type"": ""UnityEditor.ShaderGraph.Internal.Vector1ShaderProperty"",
    ""m_ObjectId"": ""{id}"",
    ""m_Guid"": {{
        ""m_GuidSerialized"": ""{Guid("guid/" + reference)}""
    }},
    ""m_Name"": ""{displayName}"",
    ""m_DefaultRefNameVersion"": 1,
    ""m_RefNameGeneratedByDisplayName"": ""{displayName}"",
    ""m_DefaultReferenceName"": ""{reference}"",
    ""m_OverrideReferenceName"": ""{reference}"",
    ""m_GeneratePropertyBlock"": true,
    ""m_UseCustomSlotLabel"": false,
    ""m_CustomSlotLabel"": """",
    ""m_DismissedVersion"": 0,
    ""m_Precision"": 0,
    ""overrideHLSLDeclaration"": false,
    ""hlslDeclarationOverride"": 0,
    ""m_Hidden"": false,
    ""m_Value"": {F(value)},
    ""m_FloatType"": {(slider ? 1 : 0)},
    ""m_RangeValues"": {{
        ""x"": {F(min)},
        ""y"": {F(max)}
    }}
}}");
        }

        public void ColorProperty(string displayName, string reference, Color value, bool hdr = true)
        {
            string id = Id("prop/" + reference);
            propertyIds.Add(id);
            objects.Add($@"{{
    ""m_SGVersion"": 3,
    ""m_Type"": ""UnityEditor.ShaderGraph.Internal.ColorShaderProperty"",
    ""m_ObjectId"": ""{id}"",
    ""m_Guid"": {{
        ""m_GuidSerialized"": ""{Guid("guid/" + reference)}""
    }},
    ""m_Name"": ""{displayName}"",
    ""m_DefaultRefNameVersion"": 1,
    ""m_RefNameGeneratedByDisplayName"": ""{displayName}"",
    ""m_DefaultReferenceName"": ""{reference}"",
    ""m_OverrideReferenceName"": ""{reference}"",
    ""m_GeneratePropertyBlock"": true,
    ""m_UseCustomSlotLabel"": false,
    ""m_CustomSlotLabel"": """",
    ""m_DismissedVersion"": 0,
    ""m_Precision"": 0,
    ""overrideHLSLDeclaration"": false,
    ""hlslDeclarationOverride"": 0,
    ""m_Hidden"": false,
    ""m_Value"": {{
        ""r"": {F(value.r)},
        ""g"": {F(value.g)},
        ""b"": {F(value.b)},
        ""a"": {F(value.a)}
    }},
    ""isMainColor"": false,
    ""m_ColorMode"": {(hdr ? 1 : 0)}
}}");
        }

        // ------------------------------------------------------------------ custom functions

        /// <summary>A fragment-stage Custom Function `name_float(inputs..., out float3 Color, out float Alpha)`.</summary>
        public void FragmentFunction(string hlslGuid, string functionName, params (string name, Input source)[] inputs)
        {
            string node = CustomFunction(hlslGuid, functionName, inputs, new[] { ("Color", 3), ("Alpha", 1) }, -400, 0);
            int colorSlot = inputs.Length;
            int alphaSlot = inputs.Length + 1;
            string baseColor = Block("SurfaceDescription.BaseColor", ColorSlot());
            string alpha = Block("SurfaceDescription.Alpha", Vector1Slot("Alpha", "Alpha", 1f, 2));
            fragmentBlocks.Add(baseColor);
            fragmentBlocks.Add(alpha);
            Edge(node, colorSlot, baseColor, 0);
            Edge(node, alphaSlot, alpha, 0);
        }

        /// <summary>A vertex-stage Custom Function `name_float(inputs..., out float3 Position)` (object space).</summary>
        public void VertexFunction(string hlslGuid, string functionName, params (string name, Input source)[] inputs)
        {
            string node = CustomFunction(hlslGuid, functionName, inputs, new[] { ("Position", 3) }, -400, -400);
            Edge(node, inputs.Length, vertexBlocks[0], 0);
        }

        string CustomFunction(string hlslGuid, string functionName, (string name, Input source)[] inputs, (string name, int dims)[] outputs, int x, int y)
        {
            string id = Id("cf/" + functionName);
            var slots = new List<string>();
            int slotIndex = 0;
            foreach ((string name, Input source) in inputs)
            {
                int dims = source == Input.UV0 || source == Input.VertexColor ? 4 : 3;
                slots.Add(VectorSlot($"{functionName}/in/{name}", slotIndex, name, dims, 0, 3));
                slotIndex++;
            }

            foreach ((string name, int dims) in outputs)
            {
                slots.Add(dims == 1 ? Vector1Slot($"{functionName}/out/{name}", name, 0f, 3, slotIndex, 1) : VectorSlot($"{functionName}/out/{name}", slotIndex, name, dims, 1, 3));
                slotIndex++;
            }

            nodeIds.Add(id);
            objects.Add($@"{{
    ""m_SGVersion"": 1,
    ""m_Type"": ""UnityEditor.ShaderGraph.CustomFunctionNode"",
    ""m_ObjectId"": ""{id}"",
    ""m_Group"": {{
        ""m_Id"": """"
    }},
    ""m_Name"": ""{functionName} (Custom Function)"",
    {DrawState(x, y, 260, 300)},
    ""m_Slots"": [{SlotList(slots)}],
    ""synonyms"": [
        ""code"",
        ""HLSL""
    ],
    ""m_Precision"": 0,
    ""m_PreviewExpanded"": false,
    ""m_DismissedVersion"": 0,
    ""m_PreviewMode"": 0,
    ""m_CustomColors"": {{
        ""m_SerializableColors"": []
    }},
    ""m_SourceType"": 0,
    ""m_FunctionName"": ""{functionName}"",
    ""m_FunctionSource"": ""{hlslGuid}"",
    ""m_FunctionSourceUsePragmas"": true,
    ""m_FunctionBody"": """"
}}");

            for (int i = 0; i < inputs.Length; i++)
            {
                Edge(InputNode(inputs[i].source), 0, id, i);
            }

            return id;
        }

        string InputNode(Input source)
        {
            if (inputNodes.TryGetValue(source, out string existing))
            {
                return existing;
            }

            string id = Id("in/" + source);
            int y = -300 + inputNodes.Count * 140;
            string body;
            switch (source)
            {
                case Input.UV0:
                    body = Node(id, "UnityEditor.ShaderGraph.UVNode", 0, "UV", y, VectorSlot("in/" + source, 0, "Out", 4, 1, 3),
                        @"""m_OutputChannel"": 0");
                    break;
                case Input.VertexColor:
                    body = Node(id, "UnityEditor.ShaderGraph.VertexColorNode", 0, "Vertex Color", y, VectorSlot("in/" + source, 0, "Out", 4, 1, 3), null);
                    break;
                case Input.PositionOS:
                case Input.PositionWS:
                    body = Node(id, "UnityEditor.ShaderGraph.PositionNode", 1, "Position", y, VectorSlot("in/" + source, 0, "Out", 3, 1, 3),
                        $@"""m_Space"": {(source == Input.PositionOS ? 0 : 2)},
    ""m_PositionSource"": 0");
                    break;
                case Input.NormalOS:
                case Input.NormalWS:
                    body = Node(id, "UnityEditor.ShaderGraph.NormalVectorNode", 0, "Normal Vector", y, VectorSlot("in/" + source, 0, "Out", 3, 1, 3),
                        $@"""m_Space"": {(source == Input.NormalOS ? 0 : 2)}");
                    break;
                default:
                    body = Node(id, "UnityEditor.ShaderGraph.ViewDirectionNode", 1, "View Direction", y, VectorSlot("in/" + source, 0, "Out", 3, 1, 3),
                        @"""m_Space"": 2");
                    break;
            }

            objects.Add(body);
            nodeIds.Add(id);
            inputNodes[source] = id;
            return id;
        }

        string Node(string id, string type, int version, string name, int y, string slot, string extra)
        {
            return $@"{{
    ""m_SGVersion"": {version},
    ""m_Type"": ""{type}"",
    ""m_ObjectId"": ""{id}"",
    ""m_Group"": {{
        ""m_Id"": """"
    }},
    ""m_Name"": ""{name}"",
    {DrawState(-900, y, 206, 131)},
    ""m_Slots"": [{SlotList(new List<string> { slot })}],
    ""synonyms"": [],
    ""m_Precision"": 0,
    ""m_PreviewExpanded"": false,
    ""m_DismissedVersion"": 0,
    ""m_PreviewMode"": 2,
    ""m_CustomColors"": {{
        ""m_SerializableColors"": []
    }}{(extra != null ? ",\n    " + extra : "")}
}}";
        }

        // ------------------------------------------------------------------ slots and blocks

        /// <summary>Adds a slot object and returns its id.</summary>
        string VectorSlot(string key, int slotId, string name, int dims, int slotType, int stage)
        {
            string id = Id("slot/" + key);
            string type = dims == 4 ? "Vector4MaterialSlot" : dims == 2 ? "Vector2MaterialSlot" : "Vector3MaterialSlot";
            string zero = dims == 4 ? @"{ ""x"": 0, ""y"": 0, ""z"": 0, ""w"": 0 }" : dims == 2 ? @"{ ""x"": 0, ""y"": 0 }" : @"{ ""x"": 0, ""y"": 0, ""z"": 0 }";
            objects.Add($@"{{
    ""m_SGVersion"": 0,
    ""m_Type"": ""UnityEditor.ShaderGraph.{type}"",
    ""m_ObjectId"": ""{id}"",
    ""m_Id"": {slotId},
    ""m_DisplayName"": ""{name}"",
    ""m_SlotType"": {slotType},
    ""m_Hidden"": false,
    ""m_ShaderOutputName"": ""{name}"",
    ""m_StageCapability"": {stage},
    ""m_Value"": {zero},
    ""m_DefaultValue"": {zero},
    ""m_Labels"": []
}}");
            return id;
        }

        string Vector1Slot(string key, string name, float value, int stage, int slotId = 0, int slotType = 0)
        {
            string id = Id("slot/" + key);
            objects.Add($@"{{
    ""m_SGVersion"": 0,
    ""m_Type"": ""UnityEditor.ShaderGraph.Vector1MaterialSlot"",
    ""m_ObjectId"": ""{id}"",
    ""m_Id"": {slotId},
    ""m_DisplayName"": ""{name}"",
    ""m_SlotType"": {slotType},
    ""m_Hidden"": false,
    ""m_ShaderOutputName"": ""{name}"",
    ""m_StageCapability"": {stage},
    ""m_Value"": {F(value)},
    ""m_DefaultValue"": {F(value)},
    ""m_Labels"": []
}}");
            return id;
        }

        string ColorSlot()
        {
            string id = Id("slot/block/BaseColor");
            objects.Add($@"{{
    ""m_SGVersion"": 0,
    ""m_Type"": ""UnityEditor.ShaderGraph.ColorRGBMaterialSlot"",
    ""m_ObjectId"": ""{id}"",
    ""m_Id"": 0,
    ""m_DisplayName"": ""Base Color"",
    ""m_SlotType"": 0,
    ""m_Hidden"": false,
    ""m_ShaderOutputName"": ""BaseColor"",
    ""m_StageCapability"": 2,
    ""m_Value"": {{ ""x"": 0.5, ""y"": 0.5, ""z"": 0.5 }},
    ""m_DefaultValue"": {{ ""x"": 0, ""y"": 0, ""z"": 0 }},
    ""m_Labels"": [],
    ""m_ColorMode"": 0,
    ""m_DefaultColor"": {{ ""r"": 0.5, ""g"": 0.5, ""b"": 0.5, ""a"": 1 }}
}}");
            return id;
        }

        string SpaceSlot(string type, string name)
        {
            string id = Id("slot/block/" + name);
            objects.Add($@"{{
    ""m_SGVersion"": 0,
    ""m_Type"": ""UnityEditor.ShaderGraph.{type}"",
    ""m_ObjectId"": ""{id}"",
    ""m_Id"": 0,
    ""m_DisplayName"": ""{name}"",
    ""m_SlotType"": 0,
    ""m_Hidden"": false,
    ""m_ShaderOutputName"": ""{name}"",
    ""m_StageCapability"": 1,
    ""m_Value"": {{ ""x"": 0, ""y"": 0, ""z"": 0 }},
    ""m_DefaultValue"": {{ ""x"": 0, ""y"": 0, ""z"": 0 }},
    ""m_Labels"": [],
    ""m_Space"": 0
}}");
            return id;
        }

        string Block(string descriptor, string slotId)
        {
            string id = Id("block/" + descriptor);
            nodeIds.Add(id);
            objects.Add($@"{{
    ""m_SGVersion"": 0,
    ""m_Type"": ""UnityEditor.ShaderGraph.BlockNode"",
    ""m_ObjectId"": ""{id}"",
    ""m_Group"": {{
        ""m_Id"": """"
    }},
    ""m_Name"": ""{descriptor}"",
    {DrawState(0, 0, 0, 0)},
    ""m_Slots"": [{SlotList(new List<string> { slotId })}],
    ""synonyms"": [],
    ""m_Precision"": 0,
    ""m_PreviewExpanded"": true,
    ""m_DismissedVersion"": 0,
    ""m_PreviewMode"": 0,
    ""m_CustomColors"": {{
        ""m_SerializableColors"": []
    }},
    ""m_SerializedDescriptor"": ""{descriptor}""
}}");
            return id;
        }

        void Edge(string fromNode, int fromSlot, string toNode, int toSlot)
        {
            edges.Add($@"        {{
            ""m_OutputSlot"": {{
                ""m_Node"": {{
                    ""m_Id"": ""{fromNode}""
                }},
                ""m_SlotId"": {fromSlot}
            }},
            ""m_InputSlot"": {{
                ""m_Node"": {{
                    ""m_Id"": ""{toNode}""
                }},
                ""m_SlotId"": {toSlot}
            }}
        }}");
        }

        // ------------------------------------------------------------------ output

        /// <summary>The vertex blocks (Position, Normal, Tangent) always exist; call before the functions.</summary>
        public void Begin()
        {
            vertexBlocks.Add(Block("VertexDescription.Position", SpaceSlot("PositionMaterialSlot", "Position")));
            vertexBlocks.Add(Block("VertexDescription.Normal", SpaceSlot("NormalMaterialSlot", "Normal")));
            vertexBlocks.Add(Block("VertexDescription.Tangent", SpaceSlot("TangentMaterialSlot", "Tangent")));
        }

        public string Build()
        {
            string graphId = Id("graph");
            string categoryId = Id("category");
            string targetId = Id("target");
            string subTargetId = Id("subtarget");

            var sb = new StringBuilder();
            sb.Append($@"{{
    ""m_SGVersion"": 3,
    ""m_Type"": ""UnityEditor.ShaderGraph.GraphData"",
    ""m_ObjectId"": ""{graphId}"",
    ""m_Properties"": [{IdList(propertyIds)}],
    ""m_Keywords"": [],
    ""m_Dropdowns"": [],
    ""m_CategoryData"": [{IdList(new List<string> { categoryId })}],
    ""m_Nodes"": [{IdList(nodeIds)}],
    ""m_GroupDatas"": [],
    ""m_StickyNoteDatas"": [],
    ""m_Edges"": [
{string.Join(",\n", edges)}
    ],
    ""m_VertexContext"": {{
        ""m_Position"": {{
            ""x"": 0,
            ""y"": 0
        }},
        ""m_Blocks"": [{IdList(vertexBlocks)}]
    }},
    ""m_FragmentContext"": {{
        ""m_Position"": {{
            ""x"": 0,
            ""y"": 200
        }},
        ""m_Blocks"": [{IdList(fragmentBlocks)}]
    }},
    ""m_PreviewData"": {{
        ""serializedMesh"": {{
            ""m_SerializedMesh"": ""{{\""mesh\"":{{\""instanceID\"":0}}}}"",
            ""m_Guid"": """"
        }},
        ""preventRotation"": false
    }},
    ""m_Path"": ""{MenuPath}"",
    ""m_GraphPrecision"": 1,
    ""m_PreviewMode"": 2,
    ""m_OutputNode"": {{
        ""m_Id"": """"
    }},
    ""m_SubDatas"": [],
    ""m_ActiveTargets"": [{IdList(new List<string> { targetId })}]
}}");

            foreach (string o in objects)
            {
                sb.Append("\n\n").Append(o);
            }

            sb.Append("\n\n").Append($@"{{
    ""m_SGVersion"": 0,
    ""m_Type"": ""UnityEditor.ShaderGraph.CategoryData"",
    ""m_ObjectId"": ""{categoryId}"",
    ""m_Name"": """",
    ""m_ChildObjectList"": [{IdList(propertyIds)}]
}}");

            sb.Append("\n\n").Append($@"{{
    ""m_SGVersion"": 1,
    ""m_Type"": ""UnityEditor.Rendering.Universal.ShaderGraph.UniversalTarget"",
    ""m_ObjectId"": ""{targetId}"",
    ""m_Datas"": [],
    ""m_ActiveSubTarget"": {{
        ""m_Id"": ""{subTargetId}""
    }},
    ""m_AllowMaterialOverride"": false,
    ""m_SurfaceType"": 1,
    ""m_ZTestMode"": 4,
    ""m_ZWriteControl"": 0,
    ""m_AlphaMode"": {(int)BlendMode},
    ""m_RenderFace"": {(int)RenderFace},
    ""m_AlphaClip"": false,
    ""m_CastShadows"": false,
    ""m_ReceiveShadows"": false,
    ""m_DisableTint"": false,
    ""m_AdditionalMotionVectorMode"": 0,
    ""m_AlembicMotionVectors"": false,
    ""m_SupportsLODCrossFade"": false,
    ""m_CustomEditorGUI"": """",
    ""m_SupportVFX"": false
}}");

            sb.Append("\n\n").Append($@"{{
    ""m_SGVersion"": 2,
    ""m_Type"": ""UnityEditor.Rendering.Universal.ShaderGraph.UniversalUnlitSubTarget"",
    ""m_ObjectId"": ""{subTargetId}"",
    ""m_KeepLightingVariants"": false,
    ""m_DefaultDecalBlending"": false,
    ""m_DefaultSSAO"": false
}}");
            sb.Append("\n\n");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ helpers

        static string DrawState(int x, int y, int w, int h) => $@"""m_DrawState"": {{
        ""m_Expanded"": true,
        ""m_Position"": {{
            ""serializedVersion"": ""2"",
            ""x"": {x},
            ""y"": {y},
            ""width"": {w},
            ""height"": {h}
        }}
    }}";

        static string SlotList(List<string> ids) => IdList(ids);

        static string IdList(List<string> ids)
        {
            if (ids.Count == 0)
            {
                return "";
            }

            var sb = new StringBuilder();
            for (int i = 0; i < ids.Count; i++)
            {
                sb.Append(i == 0 ? "\n" : ",\n").Append("        {\n            \"m_Id\": \"").Append(ids[i]).Append("\"\n        }");
            }

            return sb.Append("\n    ").ToString();
        }

        static string F(float v) => v.ToString("0.######", CultureInfo.InvariantCulture);

        string Id(string key)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(graphName + "/" + key));
                var sb = new StringBuilder(32);
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }

                return sb.ToString();
            }
        }

        string Guid(string key)
        {
            string h = Id(key + "/guid");
            return $"{h.Substring(0, 8)}-{h.Substring(8, 4)}-{h.Substring(12, 4)}-{h.Substring(16, 4)}-{h.Substring(20, 12)}";
        }
    }
}
