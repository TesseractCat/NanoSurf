using System.Collections;
using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Reflection;
using UnityEngine;

using UnityEditor;
using UnityEditor.AssetImporters;
// using UnityEditor.Graphing.Util;
// using UnityEditor.ShaderGraph;
// using UnityEditor.ShaderGraph.Internal;
// using UnityEditor.ShaderGraph.Serialization;

class NanoSurfShader {
    string text;

    public string name;
    public string type;

    string cull = "Back";
    List<string> properties;
    Dictionary<string, string> passes;

    static readonly Dictionary<string, string> fragParams = new Dictionary<string, string>() {
        {"Lit", "float4 vertexColor, float3 position, inout float3 normal, float4 uv, float3 tangentViewDir, float4 screenPosition, inout float4 color, inout float smoothness, inout float metallic, inout float3 emission, inout float ao"},
        {"Decal", "float4 vertexColor, float3 position, float3 normal, float4 uv, float3 tangentViewDir, inout float4 color"},
        {"Unlit", "float4 vertexColor, float3 position, float3 normal, float4 uv, float3 tangentViewDir, inout float4 color"},
    };
    static string geometryShaderStruct = @"
            float4 positionCS : SV_POSITION;
            float3 positionWS : INTERP0;
            float3 normalWS : INTERP1;
            float4 tangentWS : INTERP2;
            float4 uv0 : INTERP3;
            float4 color : INTERP4;
            float3 interp5 : INTERP5;
            float2 interp6 : INTERP6;
            float2 interp7 : INTERP7;
            float3 interp8 : INTERP8;
            float4 interp9 : INTERP9;
            float4 interp10 : INTERP10;
        #if UNITY_ANY_INSTANCING_ENABLED
            uint instanceID : CUSTOM_INSTANCE_ID;
        #endif
        #if (defined(UNITY_STEREO_MULTIVIEW_ENABLED)) || (defined(UNITY_STEREO_INSTANCING_ENABLED) && (defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE)))
            uint stereoTargetEyeIndexAsBlendIdx0 : BLENDINDICES0;
        #endif
        #if (defined(UNITY_STEREO_INSTANCING_ENABLED))
            uint stereoTargetEyeIndexAsRTArrayIdx : SV_RenderTargetArrayIndex;
        #endif
        #if defined(SHADER_STAGE_FRAGMENT) && defined(VARYINGS_NEED_CULLFACE)
            FRONT_FACE_TYPE cullFace : FRONT_FACE_SEMANTIC;
        #endif
    ";

    public NanoSurfShader(string text) {
        this.text = text;

        this.name = this.GetSetting("SHADER");
        this.type = this.GetSetting("TYPE");
        this.properties = this.GetSettings("PROPERTY");
        if (this.GetSetting("CULL") != null)
            this.cull = this.GetSetting("CULL");
        this.passes = this.GetPasses();
    }

    string PropertyToSL(string property) {
        string type = property.Split(" ", 2)[0];
        string name = property.Split(" ", 2)[1];
        string displayName = Regex.Replace(name.Replace("_", ""), "([a-z])([A-Z])", "$1 $2");

        if (type == "float4") {
            return $"{name} (\"{displayName}\", Color) = (0, 0, 0, 0)";
        } else if (type == "float") {
            return $"{name} (\"{displayName}\", Float) = 0";
        } else if (type == "bool") {
            return $"[Toggle] {name} (\"{displayName}\", Float) = 0";
        } else if (type == "int") {
            return $"{name} (\"{displayName}\", Int) = 0";
        } else if (type == "header") {
            return $"[Header({name})]";
        } else if (type == "sampler2D") {
            return $"{name} (\"{displayName}\", 2D) = \"white\"" + " {}";
        } else if (type == "sampler3D") {
            return $"{name} (\"{displayName}\", 3D) = \"\"" + " {}";
        } else {
            return $"{name} (\"{displayName}\", Vector) = (0, 0, 0, 0)";
        }
    }
    List<string> GetSettings(string settingID) {
        List<string> results = new List<string>();
        Regex rx = new Regex(@"(?:^|\n)" + settingID + @"\((.+)\)");
        MatchCollection matches = rx.Matches(text);
        results.AddRange(matches.Select(m => m.Groups[1].Value));
        return results;
    }
    string GetSetting(string settingID) {
        var temp = this.GetSettings(settingID);
        if (temp.Count > 0)
            return temp[0];
        return null;
    }
    Dictionary<string, string> GetPasses() {
        Dictionary<string, string> results = new Dictionary<string, string>();
        Regex rx = new Regex(@"(?:^|\n)PASS\((.*)\)(?:\r\n|\r|\n)((?:.|(?:\r\n|\r|\n))+?)ENDPASS\(\)");
        MatchCollection matches = rx.Matches(text);
        foreach (Match m in matches) {
            results.Add(m.Groups[1].Value, m.Groups[2].Value);
        }
        return results;
    }

    public string Compile(string templateText) {
        // foreach (var (k,v) in passes)
        //     Debug.Log(k + ": " + v);
        
        if (passes.Count == 0)
            return templateText;

        templateText = templateText.Replace($"{this.type}Template", name);
        foreach (string property in properties.AsEnumerable().Reverse()) {
            string SL = PropertyToSL(property);
            templateText = Regex.Replace(templateText, @"Properties(?:\r\n|\r|\n).+?\{", $"$&{SL} ");
        }

        templateText = templateText.Replace("Cull Back", $"Cull {this.cull}");

        string allPassCode = "";
        if (passes.ContainsKey("ALL")) {
            allPassCode = passes["ALL"];
            allPassCode = allPassCode.Replace("NS_INITIALIZE_GEOM_STRUCT", geometryShaderStruct);
        }

        foreach (var (passTag, passCodeTemplate) in passes) {
            string propertyCode = String.Join(";", properties.Where(s => {
                if (s.Contains("header"))
                    return false;
                if (Regex.Match(passCodeTemplate, $"UNITY_DEFINE_INSTANCED_PROP.+{s.Split(" ")[1]}").Success)
                    return false;
                return true;
            })).Replace("bool", "float") + ";";
            string passCode = passCodeTemplate;
            
            passCode = passCode.Replace("NS_INITIALIZE_GEOM_STRUCT", geometryShaderStruct);

            if (passTag == "") { // Default pass
                if (passCode.Contains("ns_vert")) {
                    passCode = passCode.Replace("NS_VERT_PARAMS", "inout float3 vertex, inout float3 normal, float4 uv");
                    templateText = templateText.Replace("#define NS_CALL_VERT", "ns_vert(out_vertex, out_normal, uv);");
                }
                if (passCode.Contains("ns_frag")) {
                    passCode = passCode.Replace("NS_FRAG_PARAMS", fragParams[this.type]);
                    templateText = templateText.Replace("#define NS_CALL_FRAG", $"ns_frag({ String.Join(", ", fragParams[this.type].Split(",").Select(x => x.Split(" ").Last())) });");
                }
                templateText = Regex.Replace(templateText, $"void {(this.type != "Decal" ? "vert" : "frag")}_float", $@"
                    {propertyCode}
                    {allPassCode}
                    {passCode}
                    $&");
            } else if (passTag != "ALL") { // Tagged pass
                templateText = new Regex("Pass").Replace(templateText, $@"
                    Pass {{
                        Name ""{passTag}""
                        Tags
                        {{
                            ""LightMode"" = ""{passTag}""
                        }}

                        Cull {this.cull}
                        Blend One Zero
                        ZTest LEqual
                        ZWrite On

                        HLSLPROGRAM
                        #pragma vertex ns_vert
                        #pragma fragment ns_frag
                        #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""

                        {propertyCode}
                        {allPassCode}
                        {passCode}
                        ENDHLSL
                    }}
                    $&", 1);
            }
        }

        return templateText;
    }
}

[ScriptedImporter(1, "ns")]
public class NanoSurfImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        string shaderText = File.ReadAllText(ctx.assetPath, Encoding.UTF8);
        NanoSurfShader nanoSurfShader = new NanoSurfShader(shaderText);

        string templateGraphPath = Directory.GetCurrentDirectory() + $"\\Packages\\com.tess.nanosurf\\Templates\\{nanoSurfShader.type}Template.shader";
        string templateGraphText = File.ReadAllText(templateGraphPath, Encoding.UTF8);

        Shader shader = ShaderUtil.CreateShaderAsset(ctx, nanoSurfShader.Compile(templateGraphText), true);
        ctx.AddObjectToAsset("MainShader", shader);
        ctx.SetMainObject(shader);
    }

    [MenuItem("Assets/Create/NanoSurf Shader")]
    static void CreateNanoSurfShader(MenuCommand menuCommand) {
        ProjectWindowUtil.CreateScriptAssetFromTemplateFile(Directory.GetCurrentDirectory() + "\\Packages\\com.tess.nanosurf\\Default.ns", "Untitled.ns");
    }

    //TODO: https://github.com/bzgeb/CustomForwardPassLightingShaderGraph/blob/master/Assets/CustomForwardPass/Editor/ShaderGraphConversionTool.cs
}