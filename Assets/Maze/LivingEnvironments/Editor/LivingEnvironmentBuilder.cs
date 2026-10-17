#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEditor;using UnityEngine;
namespace Maze.EditorTools {
 public static class LivingEnvironmentBuilder {
  const string Root="Assets/Maze/LivingEnvironments";
  static readonly Dictionary<string,string> Palette=new(){{"Ivory","#E7E1CF"},{"White","#E6EDF0"},{"Navy","#172D52"},{"Blue","#426799"},{"Floor","#AEB8C7"},{"Seam","#6F819D"},{"Gold","#FFC347"},{"Cyan","#29D6EF"},{"Glass","#56BDEC"},{"Wood","#D3B58C"},{"Leaf","#67B64E"},{"LeafDark","#357959"},{"Earth","#B79978"},{"Garden","#7BCF50"},{"Ice","#C4E6FC"},{"Rock","#7994BC"},{"Desert","#FFB45C"},{"Cloud","#FFE8DD"},{"Sky","#9DDCF5"},{"Ceiling","#E7E1CF"},{"LightWarm","#FFD36C"},{"LightCyan","#64EEFF"},{"HoloGlass","#43D7F6"},{"Deck","#DDE2E5"}};
  static bool Moving(Transform t)=>(t.name.StartsWith("HoloIsland")&&!t.name.Contains(" "))||t.name.StartsWith("CloudDrift")||t.name=="ServiceShoulder";
  static void Batch(GameObject root,string room){
   Directory.CreateDirectory(Root+"/Meshes");AssetDatabase.Refresh();var groups=new Dictionary<(Transform,Material),List<CombineInstance>>();var originals=new List<MeshFilter>();
   foreach(var mf in root.GetComponentsInChildren<MeshFilter>()){var renderer=mf.GetComponent<MeshRenderer>();if(renderer==null)continue;Transform group=root.transform;for(var t=mf.transform;t!=root.transform;t=t.parent)if(Moving(t)){group=t;break;}for(int sub=0;sub<mf.sharedMesh.subMeshCount;sub++){var key=(group,renderer.sharedMaterials[sub]);if(!groups.TryGetValue(key,out var list)){list=new();groups[key]=list;}list.Add(new CombineInstance{mesh=mf.sharedMesh,subMeshIndex=sub,transform=group.worldToLocalMatrix*mf.transform.localToWorldMatrix});}originals.Add(mf);}
   int n=0;foreach(var row in groups){var mesh=new Mesh{name=room+" batch "+n,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.CombineMeshes(row.Value.ToArray(),true,true);mesh.RecalculateBounds();string path=Root+"/Meshes/"+room+"_"+n+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old!=null){EditorUtility.CopySerialized(mesh,old);EditorUtility.SetDirty(old);UnityEngine.Object.DestroyImmediate(mesh);mesh=old;}else AssetDatabase.CreateAsset(mesh,path);var go=new GameObject("RoomBatch_"+n++);go.transform.SetParent(row.Key.Item1,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=row.Key.Item2;r.shadowCastingMode=row.Key.Item2.name.Contains("Ceiling")||row.Key.Item2.name.EndsWith("Sky")||row.Key.Item2.shader.name=="Maze/LivingHologram"?UnityEngine.Rendering.ShadowCastingMode.Off:UnityEngine.Rendering.ShadowCastingMode.On;r.receiveShadows=true;}
   foreach(var mf in originals){UnityEngine.Object.DestroyImmediate(mf.GetComponent<MeshRenderer>());UnityEngine.Object.DestroyImmediate(mf);}
  }
  public static void Review(){SeedIsolatedReview();RecordInputs();BlueUiProof.Run();}
  public static void ImportAndProof(){Import();SeedIsolatedReview();RecordInputs();BlueUiProof.Run();}
  static void SeedIsolatedReview(){string seed=Environment.GetEnvironmentVariable("LIVING_REVIEW_PROGRESS_SEED");if(string.IsNullOrEmpty(seed))return;if(PlayerSettings.companyName!="CodexIsolatedReview")throw new InvalidOperationException("Progress seeding is restricted to the isolated review preferences");if(!PlayerPrefs.HasKey("ms.progress")){PlayerPrefs.SetString("ms.progress",File.ReadAllText(seed));PlayerPrefs.Save();}}
  [Serializable] class HashEntry {public string path,sha256;}
  [Serializable] class HashRecord {public string utc;public HashEntry[] files;}
  static void RecordInputs(){
   string output=Environment.GetEnvironmentVariable("LIVING_INPUT_MANIFEST");
   if(string.IsNullOrEmpty(output))return;
   var files=new HashSet<string>(Directory.GetFiles("Assets/Maze","*.cs",SearchOption.AllDirectories));
   var ext=new HashSet<string>{".cs",".shader",".png",".json",".asset",".prefab",".glb",".fbx"};
   foreach(var dir in new[]{Root,"Assets/Maze/CryoSoft","Assets/Maze/CryoProps","Assets/Maze/Resources/CryoSoft","Assets/Maze/Scripts"})
    foreach(var file in Directory.GetFiles(dir,"*",SearchOption.AllDirectories))if(ext.Contains(Path.GetExtension(file)))files.Add(file);
   string baseline=Environment.GetEnvironmentVariable("LIVING_UI_MANIFEST");
   if(!string.IsNullOrEmpty(baseline))foreach(var row in JsonUtility.FromJson<HashRecord>("{\"files\":"+File.ReadAllText(baseline)+"}").files)files.Add(row.path);
   files.Add("Packages/manifest.json");
   var result=new HashRecord{utc=DateTime.UtcNow.ToString("O"),files=files.OrderBy(f=>f).Select(f=>new HashEntry{path=f,sha256=BitConverter.ToString(System.Security.Cryptography.SHA256.Create().ComputeHash(File.ReadAllBytes(f))).Replace("-","").ToLowerInvariant()}).ToArray()};
   File.WriteAllText(output,JsonUtility.ToJson(result,true));Debug.Log("Recorded stable validation inputs: "+files.Count);
  }
  public static void Import(){
   AssetDatabase.Refresh();Directory.CreateDirectory(Root+"/Materials");Directory.CreateDirectory(Root+"/Resources/LivingEnvironments");AssetDatabase.Refresh();
   var shader=Shader.Find("Maze/LivingSurface");if(shader==null)throw new Exception("Living surface shader missing");
   foreach(string room in new[]{"CommandShip","ArmoryWorkshop"}){
    string aoPath=Root+"/Source/"+room+"_contact.png";var texImp=AssetImporter.GetAtPath(aoPath) as TextureImporter;if(texImp!=null){texImp.sRGBTexture=false;texImp.mipmapEnabled=false;texImp.wrapMode=TextureWrapMode.Clamp;texImp.maxTextureSize=512;texImp.SaveAndReimport();}
    string path=Root+"/Source/"+room+".fbx";var imp=(ModelImporter)AssetImporter.GetAtPath(path);imp.globalScale=1;imp.useFileScale=true;imp.importAnimation=false;imp.importCameras=false;imp.importLights=false;imp.addCollider=false;imp.isReadable=false;imp.importNormals=ModelImporterNormals.Import;imp.importTangents=ModelImporterTangents.CalculateMikk;imp.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;imp.SaveAndReimport();
    var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);var go=UnityEngine.Object.Instantiate(model);go.name=room;go.transform.localScale=new Vector3(-1,1,1);
    var mats=new Dictionary<string,Material>();foreach(var row in Palette){string n="Living_"+row.Key;string mp=Root+"/Materials/"+room+"_"+n+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(mp);if(mat==null){mat=new Material(shader);AssetDatabase.CreateAsset(mat,mp);}else mat.shader=shader;if(row.Key=="HoloGlass"){mat.shader=Shader.Find("Maze/LivingHologram");mat.renderQueue=3100;}ColorUtility.TryParseHtmlString(row.Value,out var color);if(row.Key=="HoloGlass")color.a=.10f;mat.SetColor("_BaseColor",color);if(row.Key=="HoloGlass"){mats[n]=mat;EditorUtility.SetDirty(mat);continue;}mat.SetFloat("_VertexAO",.85f);mat.SetFloat("_CeilingFill",row.Key=="Ceiling"?1:0);mat.SetFloat("_Sky",row.Key=="Sky"?1:0);mat.SetFloat("_FloorAO",row.Key is "Floor" or "Seam" or "Deck"?1:0);mat.SetTexture("_ContactMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Source/"+room+"_contact.png"));mat.SetColor("_EmissionColor",row.Key is "LightCyan" or "LightWarm"?color*.5f:row.Key=="Cyan"?color*.10f:Color.black);mats[n]=mat;EditorUtility.SetDirty(mat);}
    foreach(var r in go.GetComponentsInChildren<Renderer>()){r.sharedMaterials=r.sharedMaterials.Select(m=>mats[m.name.Split('.')[0]]).ToArray();r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;r.receiveShadows=true;}
    Batch(go,room);PrefabUtility.SaveAsPrefabAsset(go,Root+"/Resources/LivingEnvironments/"+room+".prefab");UnityEngine.Object.DestroyImmediate(go);
    Debug.Log("LivingEnvironmentBuilder imported "+room);
   }
   AssetDatabase.SaveAssets();Debug.Log("LivingEnvironmentBuilder completed");
  }
 }
}
#endif
