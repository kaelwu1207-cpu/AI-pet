Texture2D<float4> Input : register(t0);
RWStructuredBuffer<uint2> History : register(u0); // RGB24 + masked flag; observed stable ms
struct Tile { uint visible, stable, edges, maxAge, edgeAge, changed, luma, masked; };
RWStructuredBuffer<Tile> Output : register(u1);
cbuffer Parameters : register(b0) {
    uint Width, Height, Columns, DeltaMs;
    uint Reset, MaskCount, ThresholdMs, TileSize;
    int4 Masks[16];
};
groupshared uint Totals[8];
[numthreads(16,16,1)]
void main(uint3 thread : SV_DispatchThreadID, uint3 group : SV_GroupID, uint lane : SV_GroupIndex) {
    if(lane < 8) Totals[lane]=0;
    GroupMemoryBarrierWithGroupSync();
    for(uint by=0;by<TileSize/16;by++) for(uint bx=0;bx<TileSize/16;bx++) {
    uint3 p=uint3(group.xy*TileSize+thread.xy%16+uint2(bx,by)*16,0);
    if(p.x<Width && p.y<Height) {
        uint index=p.y*Width+p.x;
        uint2 old=Reset!=0?uint2(0,0):History[index];
        bool masked=false;
        for(uint m=0;m<MaskCount;m++) {
            int4 r=Masks[m];
            masked=masked || (int(p.x)>=r.x && int(p.y)>=r.y && int(p.x)<r.x+r.z && int(p.y)<r.y+r.w);
        }
        if(masked) { History[index]=uint2(old.x|0x80000000,old.y); InterlockedAdd(Totals[7],1); }
        else {
            float3 color=saturate(Input.Load(int3(p.xy,0)).rgb);
            uint3 rgb=(uint3)round(color*255);
            uint3 prev=uint3(old.x&255,(old.x>>8)&255,(old.x>>16)&255);
            bool changed=Reset!=0 || (old.x&0x80000000)!=0 || any(abs(int3(rgb)-int3(prev))>PIXEL_DELTA);
            uint age=changed?0:min(old.y+DeltaMs,AGE_CAP);
            History[index]=uint2(rgb.x|(rgb.y<<8)|(rgb.z<<16),age);
            float3 right=Input.Load(int3(min(p.x+1,Width-1),p.y,0)).rgb;
            float3 down=Input.Load(int3(p.x,min(p.y+1,Height-1),0)).rgb;
            float brightness=max(color.r,max(color.g,color.b));
            float3 diff=max(abs(color-right),abs(color-down));
            bool edge=brightness>EDGE_BRIGHTNESS && max(diff.r,max(diff.g,diff.b))>EDGE_CONTRAST;
            InterlockedAdd(Totals[0],1);
            InterlockedAdd(Totals[1],age>=ThresholdMs?1:0);
            InterlockedAdd(Totals[2],edge && age>=ThresholdMs?1:0);
            InterlockedMax(Totals[3],age);
            InterlockedMax(Totals[4],edge?age:0);
            InterlockedAdd(Totals[5],changed?1:0);
            InterlockedAdd(Totals[6],(uint)round(brightness*255));
        }
    }
    }
    GroupMemoryBarrierWithGroupSync();
    if(lane==0) {
        Tile t;
        t.visible=Totals[0];t.stable=Totals[1];t.edges=Totals[2];t.maxAge=Totals[3];
        t.edgeAge=Totals[4];t.changed=Totals[5];t.luma=Totals[6];t.masked=Totals[7];
        Output[group.y*Columns+group.x]=t;
    }
}
