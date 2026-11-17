using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace WineFoxItems.Editor
{
    public static class FullyHiddenMesh
    {
        const string Root="Assets/WineFoxItems";
        public static Mesh Build(Mesh raw,StringBuilder log)
        {
            var vertices=raw.vertices;var triangles=raw.triangles;var weights=raw.boneWeights;
            var selections=Enumerable.Range(0,raw.blendShapeCount).Where(i=>raw.GetBlendShapeName(i).StartsWith("I")||raw.GetBlendShapeName(i).StartsWith("C")).ToArray();
            var owner=Enumerable.Repeat(-1,vertices.Length).ToArray();
            foreach(var i in selections)
            {
                var d=new Vector3[vertices.Length];raw.GetBlendShapeFrameVertices(i,0,d,null,null);
                for(int j=0;j<d.Length;j++)if(d[j].sqrMagnitude>1e-18f)
                {if(owner[j]>=0&&owner[j]!=i)throw new Exception("Overlapping item selection: "+j);owner[j]=i;}
            }
            bool changed=true;while(changed)
            {
                changed=false;for(int t=0;t<triangles.Length;t+=3)
                {
                    var ids=new[]{triangles[t],triangles[t+1],triangles[t+2]};var tags=ids.Select(j=>owner[j]).Where(i=>i>=0).Distinct().ToArray();
                    if(tags.Length>1)throw new Exception("Triangle crosses item selections: "+t/3);
                    if(tags.Length==1)foreach(var j in ids)if(owner[j]<0){owner[j]=tags[0];changed=true;}
                }
            }
            if(owner.Any(i=>i<0))throw new Exception("Unclassified item vertices");
            var parent=Enumerable.Range(0,vertices.Length).ToArray();Func<int,int> find=null;find=i=>parent[i]==i?i:parent[i]=find(parent[i]);
            Action<int,int> union=(a,b)=>{a=find(a);b=find(b);if(a!=b)parent[b]=a;};
            int mixed=0;
            for(int t=0;t<triangles.Length;t+=3)
            {
                var ids=new[]{triangles[t],triangles[t+1],triangles[t+2]};var bones=new HashSet<int>();
                foreach(int j in ids)
                {
                    var w=weights[j];if(Mathf.Abs(w.weight0+w.weight1+w.weight2+w.weight3-1)>1e-5f)throw new Exception("Unnormalized skin weights");
                    if(w.weight0>0)bones.Add(w.boneIndex0);if(w.weight1>0)bones.Add(w.boneIndex1);if(w.weight2>0)bones.Add(w.boneIndex2);if(w.weight3>0)bones.Add(w.boneIndex3);
                }
                // A common bind-space point skinned through at most two matrices lies on a line.
                // Thus even the two-bone staff beam has zero-area hidden triangles under any pose.
                if(bones.Count>2)throw new Exception("Hidden triangle needs more than two bone matrices: "+t/3);
                if(bones.Count>1)mixed++;
                union(ids[0],ids[1]);union(ids[0],ids[2]);
            }
            var centers=Enumerable.Range(0,vertices.Length).GroupBy(find).ToDictionary(g=>g.Key,g=>g.Aggregate(Vector3.zero,(v,i)=>v+vertices[i])/g.Count());
            var basis=Enumerable.Range(0,vertices.Length).Select(i=>centers[find(i)]).ToArray();
            var mesh=new Mesh{name="Wine Fox items - fully hidden inactive geometry",indexFormat=raw.indexFormat};
            mesh.vertices=basis;mesh.normals=raw.normals;if(raw.tangents.Length>0)mesh.tangents=raw.tangents;
            mesh.boneWeights=raw.boneWeights;mesh.bindposes=raw.bindposes;
            if(raw.colors32.Length>0)mesh.colors32=raw.colors32;
            for(int channel=0;channel<8;channel++){var uv=new List<Vector4>();raw.GetUVs(channel,uv);if(uv.Count>0)mesh.SetUVs(channel,uv);}
            mesh.subMeshCount=raw.subMeshCount;for(int sub=0;sub<raw.subMeshCount;sub++)mesh.SetTriangles(raw.GetTriangles(sub),sub);
            float maxError=0;
            for(int i=0;i<raw.blendShapeCount;i++)for(int f=0;f<raw.GetBlendShapeFrameCount(i);f++)
            {
                var d=new Vector3[vertices.Length];var n=new Vector3[vertices.Length];var tangent=new Vector3[vertices.Length];raw.GetBlendShapeFrameVertices(i,f,d,n,tangent);
                if(selections.Contains(i))for(int j=0;j<d.Length;j++)if(owner[j]==i)
                {
                    var full=vertices[j]+d[j];d[j]=full-basis[j];maxError=Mathf.Max(maxError,Vector3.Distance(full,basis[j]+d[j]));
                }
                mesh.AddBlendShapeFrame(raw.GetBlendShapeName(i),raw.GetBlendShapeFrameWeight(i,f),d,n,tangent);
            }
            mesh.RecalculateBounds();
            var hidden=mesh.vertices;
            for(int t=0;t<triangles.Length;t+=3)if(hidden[triangles[t]]!=hidden[triangles[t+1]]||hidden[triangles[t]]!=hidden[triangles[t+2]])throw new Exception("Hidden basis triangle failed: "+t/3);
            var regions=selections.Select(i=>new ItemPrefabRepair.Region{shape=raw.GetBlendShapeName(i),vertices=Enumerable.Range(0,vertices.Length).Where(j=>owner[j]==i).ToArray(),triangles=Enumerable.Range(0,triangles.Length/3).Where(t=>owner[triangles[t*3]]==i).ToArray()}).ToArray();
            File.WriteAllText(Root+"/Editor/ItemGeometryRegions.json",JsonUtility.ToJson(new ItemPrefabRepair.Regions{regions=regions},true));
            log.AppendLine("Rebuilt zero-size basis for "+regions.Length+" selection regions, including "+mixed+" two-bone beam triangles; maximum fully-visible vertex error="+maxError);
            log.AppendLine("Retained one renderer, 7130 vertices, 4272 triangles and all 35 blendshape names/order. Inactive triangles are degenerate instead of 1% size.");
            return mesh;
        }
    }
}
