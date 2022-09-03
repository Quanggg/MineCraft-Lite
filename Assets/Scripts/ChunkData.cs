using UnityEngine;
using System.Threading;
using System.Collections.Generic;
public class ChunkData
{
  public Vector2Int position;
  private byte[,,] blocks, light;
  public bool terrainReady { get; private set; }
  public bool startedLoadingDetails { get; private set; }
  public bool chunkReady { get; private set; }

  public bool isDirty;

  const int STRUCTURE_CHANCE_TREE = (int.MaxValue / 100);
  const int STRUCTURE_CHANCE_WELL = (int.MaxValue / 512);
  const int STRUCTURE_CHANCE_CAVE_ENTRANCE = (int.MaxValue / 50);


  private Thread loadTerrainThread;
  private Thread loadDetailsThread;

  public HashSet<Vector2Int> references;

  public List<StructureInfo> structures;

  public Dictionary<Vector3Int, byte> lightSources;

  public byte[,] highestNonAirBlock;

  public ChunkSaveData saveData;

  private ChunkData front, left, back, right;

  public struct StructureInfo
  {
    public StructureInfo(Vector3Int position, Structure.Type type, int seed)
    {
      this.position = position;
      this.type = type;
      this.seed = seed;
    }
    public Vector3Int position;
    public Structure.Type type;
    public int seed;
  }


  public ChunkData(Vector2Int position)
  {
    this.position = position;
    terrainReady = false;
    startedLoadingDetails = false;
    chunkReady = false;
    isDirty = false;
    references = new HashSet<Vector2Int>();
    lightSources = new Dictionary<Vector3Int, byte>();
    highestNonAirBlock = new byte[16, 16];
  }

  public byte[,,] GetBlocks()
  {
    return blocks;
  }

  public byte[,,] GetLights()
  {
    return light;
  }

  public byte[,,] NewLights()
  {
    light = new byte[16, 256, 16];
    return light;
  }

  public void StartTerrainLoading()
  {
    loadTerrainThread = new Thread(LoadTerrain);
    loadTerrainThread.IsBackground = true;
    loadTerrainThread.Start();
  }

  public void StartDetailsLoading(ChunkData front, ChunkData left, ChunkData back, ChunkData right)
  {
    this.front = front;
    this.left = left;
    this.right = right;
    this.back = back;

    loadDetailsThread = new Thread(LoadDetails);
    loadDetailsThread.IsBackground = true;
    loadDetailsThread.Start();
    startedLoadingDetails = true;
  }

  public void LoadTerrain()
  {
    blocks = new byte[16, 256, 16];
    light = new byte[16, 256, 16];
    Vector2Int worldPos = position * 16;

    for (int z = 0; z < 16; ++z)
    {
      for (int x = 0; x < 16; ++x)
      {
        int noiseX = worldPos.x + x;
        int noiseZ = worldPos.y + z;
        float height = SimplexNoise.Noise.CalcPixel2D(noiseX, noiseZ + 50000, 0.01f);
        height = height * 16 + 64;
        int heightInt = (int)height;

        float bedrock = SimplexNoise.Noise.CalcPixel2D(noiseX, noiseZ + 50000, 1f);
        bedrock = bedrock * 3 + 1;
        int bedrockInt = (int)bedrock;

        for (int y = 0; y < 256; ++y)
        {
          if (y < bedrockInt)
          {
            blocks[x, y, z] = BlockTypes.BEDROCK;
            continue;
          }
          if (y > heightInt)
          {
            blocks[x, y, z] = BlockTypes.AIR;
            continue;
          }

          float o1 = SimplexNoise.Noise.CalcPixel3D(noiseX + 50000, y, noiseZ, 0.1f);
          float o2 = SimplexNoise.Noise.CalcPixel3D(noiseX + 40000, y, noiseZ, 0.1f);
          float o3 = SimplexNoise.Noise.CalcPixel3D(noiseX + 30000, y, noiseZ, 0.04f);
          float o4 = SimplexNoise.Noise.CalcPixel3D(noiseX + 60000, y, noiseZ, 0.1f);
          float o5 = SimplexNoise.Noise.CalcPixel3D(noiseX + 70000, y, noiseZ, 0.1f);
          float o6 = SimplexNoise.Noise.CalcPixel3D(noiseX + 80000, y, noiseZ, 0.03f);

          float heightGradient = Mathf.Pow(Mathf.Clamp01(y / 128f), 2f);

          float c1 = SimplexNoise.Noise.CalcPixel3D(noiseX, y, noiseZ, 0.1f);
          float c2 = SimplexNoise.Noise.CalcPixel3D(noiseX, y, noiseZ, 0.04f);
          float c3 = SimplexNoise.Noise.CalcPixel3D(noiseX, y, noiseZ, 0.02f);
          float c4 = SimplexNoise.Noise.CalcPixel3D(noiseX, y, noiseZ, 0.01f);

          c1 += (heightGradient);
          if (c1 < .5 && c2 < .5 && c3 < .5 && c4 < .5)
          {
            blocks[x, y, z] = BlockTypes.AIR;
            continue;
          }

          if (y == heightInt)
          {
            blocks[x, y, z] = BlockTypes.GRASS;
            continue;
          }

          if (y >= heightInt - 4)
          {
            blocks[x, y, z] = BlockTypes.DIRT;
            continue;
          }

          o5 += (heightGradient);
          if (y < 64 && o5 < .04)
          {
            blocks[x, y, z] = BlockTypes.GOLD;
            continue;
          }

          if (y < 16 && Mathf.Pow(o2, 4f) > .7 && o3 < .1)
          {
            blocks[x, y, z] = BlockTypes.DIAMOND;
            continue;
          }

          if (o4 < .1 && o6 > .8)
          {
            blocks[x, y, z] = BlockTypes.IRON;
            continue;
          }

          if (o1 < .08)
          {
            blocks[x, y, z] = BlockTypes.COAL;
            continue;
          }

          blocks[x, y, z] = BlockTypes.STONE;
          continue;
        }
      }
    }

    string hash = World.activeWorld.info.seed.ToString() + position.x.ToString() + position.y.ToString();
    int structuresSeed = hash.GetHashCode();
    System.Random rnd = new System.Random(structuresSeed);
    structures = new List<StructureInfo>();
    bool[,] spotsTaken = new bool[16, 16];

    if (rnd.Next() < STRUCTURE_CHANCE_CAVE_ENTRANCE)
    {

      int h = 255;
      while (h > 0)
      {
        if (blocks[8, h, 8] != BlockTypes.AIR)
        {
          structures.Add(new StructureInfo(new Vector3Int(0, h + 6, 0), Structure.Type.CAVE_ENTRANCE, rnd.Next()));
          break;
        }
        h--;
      }
    }

    for (int y = 2; y < 14; ++y)
    {
      for (int x = 2; x < 14; ++x)
      {
        if (rnd.Next() < STRUCTURE_CHANCE_TREE)
        {
          if (IsSpotFree(spotsTaken, new Vector2Int(x, y), 2))
          {
            spotsTaken[x, y] = true;
            int height = 255;
            while (height > 0)
            {
              if (blocks[x, height, y] == BlockTypes.GRASS)
              {
                structures.Add(new StructureInfo(new Vector3Int(x, height + 1, y), Structure.Type.OAK_TREE, rnd.Next()));
                break;
              }
              height--;
            }
          }
        }
      }
    }

    if (rnd.Next() < STRUCTURE_CHANCE_WELL)
    {
      if (IsSpotFree(spotsTaken, new Vector2Int(7, 7), 3))
      {
        int minH = 255;
        int maxH = 0;
        bool canPlace = true;
        for (int y = 5; y < 11; ++y)
        {
          for (int x = 5; x < 11; ++x)
          {
            for (int h = 255; h > -1; h--)
            {
              byte b = blocks[x, h, y];
              if (b != BlockTypes.AIR)
              {
                canPlace &= (b == BlockTypes.GRASS);
                minH = Mathf.Min(minH, h);
                maxH = Mathf.Max(maxH, h);
                break;
              }
            }
          }
        }
        canPlace &= Mathf.Abs(minH - maxH) < 2;
        if (canPlace)
        {
          for (int y = 5; y < 11; ++y)
          {
            for (int x = 5; x < 11; ++x)
            {
              spotsTaken[x, y] = true;
            }
          }
          int h = 255;
          while (h > 0)
          {
            if (blocks[7, h, 7] != BlockTypes.AIR)
            {
              structures.Add(new StructureInfo(new Vector3Int(7, h + 1, 7), Structure.Type.WELL, rnd.Next()));
              break;
            }
            h--;
          }
        }
      }
    }

    saveData = SaveDataManager.instance.Load(position);

    terrainReady = true;
  }

  private bool IsSpotFree(bool[,] spotsTaken, Vector2Int position, int size)
  {
    bool spotTaken = false;
    for (int y = Mathf.Max(0, position.y - size); y < Mathf.Min(15, position.y + size + 1); ++y)
    {
      for (int x = Mathf.Max(0, position.x - size); x < Mathf.Min(15, position.x + size + 1); ++x)
      {
        spotTaken |= spotsTaken[x, y];
      }
    }
    return !spotTaken;
  }

  private void LoadDetails()
  {
    for (int i = 0; i < structures.Count; ++i)
    {
      StructureInfo structure = structures[i];
      bool overwritesEverything = Structure.OverwritesEverything(structure.type);
      Vector3Int p = structure.position;
      int x = p.x;
      int y = p.y;
      int z = p.z;
      List<Structure.Change> changeList = Structure.Generate(structure.type, structure.seed);
      for (int j = 0; j < changeList.Count; ++j)
      {
        Structure.Change c = changeList[j];
        int placeX = x + c.x;
        int placeY = y + c.y;
        int placeZ = z + c.z;

        if (!overwritesEverything)
        {
          if (blocks[placeX, placeY, placeZ] < BlockTypes.density[c.b]) continue;
        }

        blocks[placeX, placeY, placeZ] = c.b;
      }
    }

    front = null;
    left = null;
    right = null;
    back = null;

    List<ChunkSaveData.C> changes = saveData.changes;
    for (int i = 0; i < changes.Count; ++i)
    {
      ChunkSaveData.C c = changes[i];
      blocks[c.x, c.y, c.z] = c.b;
      byte lightLevel = BlockTypes.lightLevel[c.b];
      if (lightLevel > 0)
      {
        lightSources.Add(new Vector3Int(c.x, c.y, c.z), lightLevel);
      }
    }

    for (int z = 0; z < 16; ++z)
    {
      for (int x = 0; x < 16; ++x)
      {
        highestNonAirBlock[x, z] = 0;
        for (int y = 255; y > -1; --y)
        {
          if (blocks[x, y, z] != BlockTypes.AIR)
          {
            highestNonAirBlock[x, z] = (byte)y;
            break;
          }
        }
      }
    }
    chunkReady = true;
  }

  public void Modify(int x, int y, int z, byte blockType)
  {
    if (!chunkReady) throw new System.Exception("Chunk has not finished loading");

    saveData.changes.Add(new ChunkSaveData.C((byte)x, (byte)y, (byte)z, blockType));
    blocks[x, y, z] = blockType;
    if (blockType == BlockTypes.AIR)
    {
      if (highestNonAirBlock[x, z] == y)
      {
        highestNonAirBlock[x, z] = 0;
        for (int yy = y; yy > -1; yy--)
        {
          if (blocks[x, yy, z] != BlockTypes.AIR)
          {
            highestNonAirBlock[x, z] = (byte)yy;
            break;
          }
        }
      }
    }
    else
    {
      highestNonAirBlock[x, z] = (byte)Mathf.Max(highestNonAirBlock[x, z], y);
    }
  }

  public void Unload()
  {
    if (isDirty)
    {
      SaveDataManager.instance.Save(saveData);
    }
  }
}