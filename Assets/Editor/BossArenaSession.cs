using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

[InitializeOnLoad]
public static class BossArenaSession {
 const string Dir="C:/PirateGame/Temp/BossArenaDesign/";
 const string ScenePath="Assets/Scenes/Island1_BossArea_Harbor.unity";
 static BossArenaSession(){EditorApplication.update += Tick;}
 static void Tick(){
  if(EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Dir+"command.json"))return;
  string s=File.ReadAllText(Dir+"command.json");File.Delete(Dir+"command.json");
  try{
   if(SceneManager.GetActiveScene().path!=ScenePath)throw new Exception("Wrong active scene");
   var c=JsonUtility.FromJson<Command>(s);
   if(c.op=="survey") Survey();
   if(c.op=="refresh") AssetDatabase.Refresh();
   if(c.op=="reload") EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
   if(c.op=="view") {var sv=SceneView.lastActiveSceneView;sv.orthographic=c.ortho;sv.LookAtDirect(c.target,Quaternion.Euler(c.rotation),c.size);sv.sceneLighting=true;sv.Repaint();}
   if(c.op=="render") Render(c);
   if(c.op=="run") {var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("BossArenaPass")).First(t=>t!=null);type.GetMethod("Run").Invoke(null,null);}
   File.WriteAllText(Dir+"done.txt",c.op+" OK "+DateTime.Now);
  }catch(Exception e){File.WriteAllText(Dir+"error.txt",e.ToString());Debug.LogException(e);}
 }
 [Serializable] public class Command {public string op; public Vector3 target,rotation,position;public float size=100;public bool ortho;public string file;}
 [Serializable] public class SurveyData {public string scene;public bool dirty;public List<Tile> terrains=new();public List<Item> roots=new();public List<Item> prefabs=new();}
 [Serializable] public class Tile{public string name,path,data,material,shader;public Vector3 position,size;public int resolution;public float[] heights;}
 [Serializable] public class Item{public string name,path;public Vector3 position,rotation,scale,center,size;public int renderers,colliders;public string[] materials;}
 static string PathOf(Transform t){return t.parent==null?t.name:PathOf(t.parent)+"/"+t.name;}
 public static Bounds BoundsOf(GameObject go){var rs=go.GetComponentsInChildren<Renderer>(true).Where(r=>r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();if(rs.Length==0)return new Bounds(go.transform.position,Vector3.zero);var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);return b;}
 static Item Describe(GameObject go,string path){var b=BoundsOf(go);return new Item{name=go.name,path=path,position=go.transform.position,rotation=go.transform.eulerAngles,scale=go.transform.lossyScale,center=b.center,size=b.size,renderers=go.GetComponentsInChildren<Renderer>(true).Length,colliders=go.GetComponentsInChildren<Collider>(true).Length,materials=go.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m).Select(m=>AssetDatabase.GetAssetPath(m)+" | "+m.shader.name).Distinct().ToArray()};}
 static void Survey(){var d=new SurveyData{scene=SceneManager.GetActiveScene().path,dirty=SceneManager.GetActiveScene().isDirty};
  foreach(var t in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include,FindObjectsSortMode.None)){
   if(t.gameObject.scene.path!=ScenePath)continue;var td=t.terrainData;var a=new Tile{name=t.name,path=PathOf(t.transform),data=AssetDatabase.GetAssetPath(td),position=t.transform.position,size=td.size,resolution=td.heightmapResolution,material=t.materialTemplate?AssetDatabase.GetAssetPath(t.materialTemplate):"null",shader=t.materialTemplate?t.materialTemplate.shader.name:"null",heights=new float[65*65]};
   for(int z=0;z<65;z++)for(int x=0;x<65;x++)a.heights[z*65+x]=td.GetInterpolatedHeight(x/64f,z/64f)+t.transform.position.y;d.terrains.Add(a);
  }
  foreach(var g in SceneManager.GetActiveScene().GetRootGameObjects())d.roots.Add(Describe(g,PathOf(g.transform)));
  foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Sun_Temple/Prefabs"})) {var p=AssetDatabase.GUIDToAssetPath(guid);var g=AssetDatabase.LoadAssetAtPath<GameObject>(p);d.prefabs.Add(Describe(g,p));}
  File.WriteAllText(Dir+"survey.json",JsonUtility.ToJson(d,true));
 }
 static void Render(Command c){var go=new GameObject("Temporary arena review camera"){hideFlags=HideFlags.HideAndDontSave};var cam=go.AddComponent<Camera>();cam.transform.position=c.position;cam.transform.LookAt(c.target);cam.fieldOfView=55;cam.nearClipPlane=.2f;cam.farClipPlane=3000;cam.useOcclusionCulling=false;cam.clearFlags=CameraClearFlags.Skybox;cam.orthographic=c.ortho;cam.orthographicSize=c.size;var lods=UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Exclude);foreach(var l in lods)l.ForceLOD(0);var rt=new RenderTexture(1600,1000,24);cam.targetTexture=rt;cam.Render();var old=RenderTexture.active;RenderTexture.active=rt;var tx=new Texture2D(1600,1000,TextureFormat.RGB24,false);tx.ReadPixels(new Rect(0,0,1600,1000),0,0);tx.Apply();File.WriteAllBytes(Dir+c.file,tx.EncodeToPNG());RenderTexture.active=old;cam.targetTexture=null;UnityEngine.Object.DestroyImmediate(tx);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(go);foreach(var l in lods)l.ForceLOD(-1);}
}

