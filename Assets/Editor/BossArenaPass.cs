using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
public static class BossArenaPass {
 const string D="C:/PirateGame/Temp/BossArenaDesign/";
 const string RootName="SUN TEMPLE - Sanctuary of the Tides";
 static Terrain[] tiles;static Transform root;static Dictionary<string,string> paths;static Dictionary<Material,Material> mats;static List<GameObject> placed;
 [Serializable] public class Plan {public Entry[] items;}
 [Serializable] public class Entry {public string prefab,group,name;public float x,z,y=-999,yaw,scale=1,tilt;public bool ruin,conform,temple;public float offset=.045f;public float sx=1,sy=1,sz=1;}
 public static float H(float x,float z){var t=tiles.FirstOrDefault(a=>x>=a.transform.position.x-.001f&&x<=a.transform.position.x+a.terrainData.size.x+.001f&&z>=a.transform.position.z-.001f&&z<=a.transform.position.z+a.terrainData.size.z+.001f);if(!t)throw new Exception("Outside Boss_Area: "+x+","+z);return t.SampleHeight(new Vector3(x,0,z))+t.transform.position.y;}
 static bool Allowed(float x,float z){return tiles.Any(t=>x>=t.transform.position.x&&x<=t.transform.position.x+t.terrainData.size.x&&z>=t.transform.position.z&&z<=t.transform.position.z+t.terrainData.size.z);}
 static Transform Group(string name){var t=root.Find(name);if(t)return t;var g=new GameObject(name);g.transform.SetParent(root,false);return g.transform;}
 static Texture Tex(Material m,string property){var so=new SerializedObject(m);var ar=so.FindProperty("m_SavedProperties.m_TexEnvs");for(int i=0;i<ar.arraySize;i++){var e=ar.GetArrayElementAtIndex(i);if(e.FindPropertyRelative("first").stringValue==property)return e.FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture;}return null;}
 static Texture LoadTex(string name){return AssetDatabase.LoadAssetAtPath<Texture>("Assets/Sun_Temple/Content/Textures/"+name);}
 static Material Adapt(Material src){if(!src)return null;if(mats.ContainsKey(src))return mats[src];var m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.name="Sanctuary / "+src.name;var texture=Tex(src,"_MainTex")??Tex(src,"_BaseMap");var normal=Tex(src,"_BumpMap");m.SetTexture("_BaseMap",texture);m.SetTexture("_BumpMap",normal);if(normal)m.EnableKeyword("_NORMALMAP");m.SetFloat("_Smoothness",.16f);m.SetFloat("_Metallic",0);m.SetColor("_BaseColor",new Color(.83f,.79f,.68f,1));string n=src.name;
  if(n.Contains("Plaster")){m.SetTexture("_BaseMap",LoadTex("plaster_white_A.tif"));m.SetColor("_BaseColor",new Color(.86f,.80f,.66f));}
  if(n.Contains("Bricks"))m.SetColor("_BaseColor",new Color(.92f,.86f,.71f));
  if(n.Contains("Temple_Trims")||n.Contains("Marble"))m.SetColor("_BaseColor",new Color(.91f,.85f,.72f));
  if(n.Contains("Roof_Clay"))m.SetColor("_BaseColor",new Color(.56f,.60f,.51f));
  if(n.Contains("Roof_Gold")){m.SetColor("_BaseColor",new Color(.52f,.58f,.40f));m.SetFloat("_Metallic",.25f);}
  if((n.Contains("Fol_")&&!n.Contains("Bark"))||n.Contains("Grass")){m.SetColor("_BaseColor",new Color(.72f,.82f,.62f));m.SetFloat("_AlphaClip",1);m.SetFloat("_Cutoff",.45f);m.EnableKeyword("_ALPHATEST_ON");m.SetFloat("_Cull",0);m.renderQueue=2450;}
  if(n.Contains("Pavement"))m.SetColor("_BaseColor",new Color(.78f,.73f,.60f));
  if(n.Contains("Dec_Ornaments")){m.SetFloat("_AlphaClip",1);m.EnableKeyword("_ALPHATEST_ON");m.SetFloat("_Cull",0);m.renderQueue=2450;}
  mats[src]=m;return m;
 }
 static GameObject Put(Entry e){
  var g=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(paths[e.prefab]));g.name=e.name;g.transform.SetParent(Group(e.group),false);g.transform.SetPositionAndRotation(Vector3.zero,Quaternion.Euler(0,e.yaw,e.tilt));g.transform.localScale=new Vector3(e.sx,e.sy,e.sz)*e.scale;
  var b=BossArenaSession.BoundsOf(g);float y=e.y==-999?H(e.x,e.z)-.12f:e.y;g.transform.position+=new Vector3(e.x-b.center.x,y-b.min.y,e.z-b.center.z);
  foreach(var r in g.GetComponentsInChildren<Renderer>(true))r.sharedMaterials=r.sharedMaterials.Select(Adapt).ToArray();
  foreach(var ps in g.GetComponentsInChildren<ParticleSystem>(true))ps.gameObject.SetActive(false);foreach(var l in g.GetComponentsInChildren<Light>(true))l.enabled=false;
  foreach(var t in g.GetComponentsInChildren<Transform>(true)){if(t==g.transform)continue;bool disable=e.ruin&&(t.name.Contains("Roof")||t.name.Contains("Chimney")||t.name.StartsWith("Prop_")||t.name.Contains("WindowFlowers"));disable|=e.temple&&(t.name.StartsWith("Prop_")||t.name.StartsWith("FX_")||t.name.StartsWith("HLP_"));if(disable)t.gameObject.SetActive(false);}
  b=BossArenaSession.BoundsOf(g);foreach(float xx in new[]{b.min.x,b.center.x,b.max.x})foreach(float zz in new[]{b.min.z,b.center.z,b.max.z})if(!Allowed(xx,zz))throw new Exception("Placement crosses protected area: "+e.name+" "+b);
  if(e.conform)foreach(var f in g.GetComponentsInChildren<MeshFilter>()){
   var mesh=UnityEngine.Object.Instantiate(f.sharedMesh);mesh.name="Sanctuary terrain fitted / "+f.sharedMesh.name;var v=mesh.vertices;
   for(int i=0;i<v.Length;i++){var w=f.transform.TransformPoint(v[i]);w.y=H(w.x,w.z)+e.offset+Mathf.Clamp(w.y-b.min.y,0,.015f);v[i]=f.transform.InverseTransformPoint(w);}mesh.vertices=v;mesh.RecalculateBounds();mesh.RecalculateNormals();f.sharedMesh=mesh;var mc=f.GetComponent<MeshCollider>();if(mc)mc.sharedMesh=mesh;
  }
  placed.Add(g);return g;
 }
 public static void Run(){
  if(File.Exists(D+"audit.flag")){File.Delete(D+"audit.flag");Audit();return;}
  var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/Scenes/Island1_BossArea_Harbor.unity")throw new Exception("Wrong scene");
  tiles=UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include).Where(t=>t.name=="Boss_Area"&&t.gameObject.scene==scene).ToArray();if(tiles.Length!=3)throw new Exception("Expected surveyed three exact tiles");
  paths=AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Sun_Temple/Prefabs"}).Select(AssetDatabase.GUIDToAssetPath).ToDictionary(p=>Path.GetFileNameWithoutExtension(p),p=>p);mats=new();placed=new();
  var old=GameObject.Find(RootName);if(old)UnityEngine.Object.DestroyImmediate(old);
  var parent=scene.GetRootGameObjects().First(x=>x.name=="Sumit").transform;root=new GameObject(RootName).transform;root.SetParent(parent,false);root.position=Vector3.zero;
  foreach(var g in scene.GetRootGameObjects().Where(g=>g.name.StartsWith("Bld_")||g.name.StartsWith("Dec_Ornament"))){var b=BossArenaSession.BoundsOf(g);if(Allowed(b.min.x,b.min.z)&&Allowed(b.max.x,b.max.z)&&Allowed(b.min.x,b.max.z)&&Allowed(b.max.x,b.min.z)){Undo.RecordObject(g,"Replace previous arena blockout");g.SetActive(false);}}
  var archived=new List<string>();
  foreach(var tile in tiles)foreach(var t in tile.GetComponentsInChildren<Transform>(true)){
   if(t==tile.transform||!t.gameObject.activeInHierarchy||t.GetComponent<Terrain>()||t.GetComponentsInChildren<Renderer>(true).Length==0)continue;
   var b=BossArenaSession.BoundsOf(t.gameObject);if(b.size==Vector3.zero)continue;
   if(Allowed(b.min.x,b.min.z)&&Allowed(b.max.x,b.max.z)&&Allowed(b.min.x,b.max.z)&&Allowed(b.max.x,b.min.z)){Undo.RecordObject(t.gameObject,"Retire previous Boss_Area blockout");t.gameObject.SetActive(false);PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);archived.Add(t.name);}
  }
  File.WriteAllText(D+"retired-blockout.txt",string.Join("\n",archived));
  var ground=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="Sanctuary / original terrain - sediment"};ground.SetTexture("_BaseMap",LoadTex("ground_redclay_A.tif"));ground.SetTexture("_BumpMap",LoadTex("det_dirt3_N.tif"));ground.EnableKeyword("_NORMALMAP");ground.SetTextureScale("_BaseMap",new Vector2(22,22));ground.SetTextureScale("_BumpMap",new Vector2(22,22));ground.SetColor("_BaseColor",new Color(.90f,.88f,.71f));ground.SetFloat("_Smoothness",.08f);
  foreach(var t in tiles){Undo.RecordObject(t,"Sanctuary terrain appearance");t.materialTemplate=ground;PrefabUtility.RecordPrefabInstancePropertyModifications(t);}
  var plan=JsonUtility.FromJson<Plan>(File.ReadAllText(D+"plan.json"));foreach(var e in plan.items)Put(e);
  Physics.SyncTransforms();
  foreach(var g in placed){PrefabUtility.RecordPrefabInstancePropertyModifications(g.transform);PrefabUtility.RecordPrefabInstancePropertyModifications(g);foreach(var r in g.GetComponentsInChildren<Renderer>(true))PrefabUtility.RecordPrefabInstancePropertyModifications(r);foreach(var f in g.GetComponentsInChildren<MeshFilter>(true))PrefabUtility.RecordPrefabInstancePropertyModifications(f);foreach(var c in g.GetComponentsInChildren<MeshCollider>(true))PrefabUtility.RecordPrefabInstancePropertyModifications(c);foreach(var t in g.GetComponentsInChildren<Transform>(true))if(!t.gameObject.activeSelf)PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);}
  EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
  File.WriteAllText(D+"build.txt","Placed prefab roots: "+placed.Count+"\nEligible tiles: "+tiles.Length+"\nTerrain heightmaps: UNCHANGED\nMaterials are scene-local.\n");
  var sv=SceneView.lastActiveSceneView;if(sv){sv.LookAtDirect(new Vector3(229,13,164),Quaternion.Euler(47,145,0),92);sv.Repaint();}
 }
 public static void Audit(){
  var scene=SceneManager.GetActiveScene();var sb=new System.Text.StringBuilder();var rt=GameObject.Find(RootName).transform;
  tiles=UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include).Where(t=>t.name=="Boss_Area"&&t.gameObject.scene==scene).ToArray();
  foreach(var g in scene.GetRootGameObjects())if(g.name=="Sumit")foreach(Transform child in g.transform){if(child==rt)continue;sb.AppendLine("EXISTING CHILD "+child.name+" active="+child.gameObject.activeSelf+" pos="+child.position+" bounds="+BossArenaSession.BoundsOf(child.gameObject));}
  foreach(var t in tiles)foreach(Transform child in t.transform){if(child==rt)continue;sb.AppendLine("TILE CHILD "+child.name+" active="+child.gameObject.activeSelf+" bounds="+BossArenaSession.BoundsOf(child.gameObject));}
  int outside=0;foreach(Transform group in rt)foreach(Transform p in group){var b=BossArenaSession.BoundsOf(p.gameObject);foreach(float x in new[]{b.min.x,b.center.x,b.max.x})foreach(float z in new[]{b.min.z,b.center.z,b.max.z})if(!Allowed(x,z)){outside++;sb.AppendLine("OUTSIDE "+p.name+" "+b);}}
  sb.AppendLine("Outside footprint samples: "+outside);Physics.SyncTransforms();
  var probes=new List<Vector3>();for(int z=157;z<=186;z+=3)for(int x=210;x<=256;x+=3)if(Vector2.Distance(new Vector2(x,z),new Vector2(233,172))<18)probes.Add(new Vector3(x,H(x,z)+.7f,z));
  int obstacles=0;foreach(var p in probes){var hits=Physics.OverlapCapsule(p,p+Vector3.up*1,.45f).Where(c=>!(c is TerrainCollider)&&!c.transform.IsChildOf(rt.Find("04 - Processional paving"))).ToArray();if(hits.Length>0){obstacles++;sb.AppendLine("COMBAT OBSTACLE "+p+" "+string.Join(",",hits.Select(c=>c.name)));}}
  sb.AppendLine("Combat probes: "+probes.Count+"; obstacle probes: "+obstacles);
  foreach(var route in new[]{new[]{new Vector2(151,201),new Vector2(183,201),new Vector2(211,183)},new[]{new Vector2(249,185),new Vector2(250,210),new Vector2(250,213.8f)}}){for(int j=0;j<route.Length-1;j++){var a=route[j];var b=route[j+1];int count=Mathf.CeilToInt(Vector2.Distance(a,b));for(int i=0;i<=count;i++){var v=Vector2.Lerp(a,b,i/(float)count);var p=new Vector3(v.x,H(v.x,v.y)+.7f,v.y);var hits=Physics.OverlapCapsule(p,p+Vector3.up,.45f).Where(c=>!(c is TerrainCollider)&&!c.transform.IsChildOf(rt.Find("04 - Processional paving"))).ToArray();if(hits.Length>0)sb.AppendLine("ROUTE OBSTACLE "+p+" "+string.Join(",",hits.Select(c=>c.name)));}}}
  foreach(Transform p in rt.Find("01 - High sanctuary")){var b=BossArenaSession.BoundsOf(p.gameObject);sb.AppendLine("TEMPLE PART "+p.name+" "+b);if(p.name.Contains("ascent")){foreach(var f in p.GetComponentsInChildren<MeshFilter>()){if(p.name=="Broad temple ascent 2")File.WriteAllText(D+"stair-vertices.csv",string.Join("\n",f.sharedMesh.vertices.Select(v=>f.transform.TransformPoint(v)).Select(v=>v.x+","+v.y+","+v.z)));var vs=f.sharedMesh.vertices.Select(v=>f.transform.TransformPoint(v)).ToArray();sb.AppendLine(f.name+" north maxY="+vs.Where(v=>v.z>b.max.z-.8f).Select(v=>v.y).DefaultIfEmpty(-999).Max()+" south maxY="+vs.Where(v=>v.z<b.min.z+.8f).Select(v=>v.y).DefaultIfEmpty(-999).Max());}}}
  foreach(var r in rt.GetComponentsInChildren<Renderer>())foreach(var m in r.sharedMaterials)if(!m||!m.shader||!m.shader.isSupported)sb.AppendLine("ACTIVE MATERIAL ISSUE "+r.name+" | "+(m?m.name:"NULL"));
  sb.AppendLine("Materials: "+rt.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m).Distinct().Count());sb.AppendLine("Missing shaders: "+rt.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Count(m=>!m||!m.shader||!m.shader.isSupported));
  for(float z=132;z<=144;z+=.5f){var hits=Physics.RaycastAll(new Vector3(250,50,z),Vector3.down,60).Where(h=>h.collider.transform.IsChildOf(rt)).OrderByDescending(h=>h.point.y).ToArray();sb.AppendLine("STAIR SURFACE z="+z+" terrain="+H(250,z)+" hit="+(hits.Length>0?hits[0].point.y+" "+hits[0].collider.name:"none"));}
  File.WriteAllText(D+"audit.txt",sb.ToString());
 }

}
