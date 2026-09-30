using UnityEngine;
using UnityEngine.Rendering;

namespace PirateGame.Quests
{
    /// <summary>
    /// Creates URP unlit/lit materials in code with the right blend state. Used by the
    /// scene builder (saved as assets) and as a runtime fallback.
    /// </summary>
    public static class QuestMaterials
    {
        public const string ParticlesUnlit = "Universal Render Pipeline/Particles/Unlit";
        public const string Lit = "Universal Render Pipeline/Lit";

        public static Material Additive(Color color, Texture tex = null) => Unlit(color, tex, true);
        public static Material AlphaBlended(Color color, Texture tex = null) => Unlit(color, tex, false);

        public static Material Unlit(Color color, Texture tex, bool additive)
        {
            Shader sh = Shader.Find(ParticlesUnlit) ?? Shader.Find("Sprites/Default");
            var m = new Material(sh) { name = additive ? "QuestAdditive" : "QuestAlpha" };
            if (tex != null)
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            }
            m.color = color;
            MakeTransparent(m, additive);
            return m;
        }

        public static void MakeTransparent(Material m, bool additive)
        {
            m.SetOverrideTag("RenderType", "Transparent");
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("ShadowCaster", false);
        }

        public static Material LitColor(Color color, float smoothness = 0.3f, float metallic = 0f)
        {
            var m = new Material(Shader.Find(Lit));
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            return m;
        }

        public static Material LitEmissive(Color color, Color emission)
        {
            Material m = LitColor(color, 0.6f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return m;
        }

        public static Material LitTransparent(Color color, float smoothness = 0.9f, bool writeDepth = false)
        {
            Material m = LitColor(color, smoothness);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", writeDepth ? 1f : 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent - (writeDepth ? 50 : 0);
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("ShadowCaster", false);
            return m;
        }
    }
}
