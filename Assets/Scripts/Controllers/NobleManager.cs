using System.Collections.Generic;
using UnityEngine;

// ============================================================
// NobleManager — จัดการระบบขุนนาง (Noble) แยกออกจาก GameController
//
//   หน้าที่:
//   • Setup()      สุ่มขุนนาง 4 ใบจาก master pool แล้ววางลง left/right container
//   • CheckClaim() เช็คว่า player คนปัจจุบันมีโบนัสครบเงื่อนไขขุนนางใบไหน
//                  → ให้คะแนน, mark ขุนนางใบนั้นว่าถูก claim, เอาออกจาก active list
//
//   ออกแบบเป็น pure C# class (ไม่ใช่ MonoBehaviour) เพื่อ:
//   • รับ reference จาก GameController ผ่าน constructor — Inspector ไม่ต้องเปลี่ยน
//   • test/แก้ logic ขุนนางได้โดยไม่กระทบส่วนอื่นของเกม (Single Responsibility)
// ============================================================
public class NobleManager
{
    private readonly GameObject noblePrefab;
    private readonly Transform leftContainer;
    private readonly Transform rightContainer;
    private readonly List<NobleData> masterPool;
    private readonly List<NobleDisplay> active = new List<NobleDisplay>();

    // [Noble sync] ใบที่ spawn ทั้งหมด (รวมที่ถูก claim แล้ว — active เก็บเฉพาะที่ยังว่าง) + ใคร claim ใบไหน
    private readonly List<NobleDisplay> spawned = new List<NobleDisplay>();
    private readonly Dictionary<string, string> claimedBy = new Dictionary<string, string>();

    public IReadOnlyList<NobleDisplay> Active => active;

    public NobleManager(
        GameObject noblePrefab,
        Transform leftContainer,
        Transform rightContainer,
        List<NobleData> masterPool)
    {
        this.noblePrefab = noblePrefab;
        this.leftContainer = leftContainer;
        this.rightContainer = rightContainer;
        this.masterPool = masterPool;
    }

    /// <summary>สุ่มขุนนาง 4 ใบจาก master pool แล้ววางลงคอนเทนเนอร์ซ้าย/ขวา</summary>
    public void Setup()
    {
        if (masterPool == null || masterPool.Count < 4)
        {
            Debug.LogWarning("[NobleManager] มีขุนนางใน Master น้อยกว่า 4 ใบ! กรุณาใส่ให้ครบก่อน");
            return;
        }

        active.Clear();
        spawned.Clear();
        claimedBy.Clear();

        // ก็อปปี้ลิสต์ออกมาสับไพ่ (Fisher-Yates shuffle)
        List<NobleData> tempNobles = new List<NobleData>(masterPool);
        for (int i = 0; i < tempNobles.Count; i++)
        {
            NobleData temp = tempNobles[i];
            int randomIndex = Random.Range(i, tempNobles.Count);
            tempNobles[i] = tempNobles[randomIndex];
            tempNobles[randomIndex] = temp;
        }

        // ดึงมา 4 ใบ — 2 ใบแรกซ้าย, 2 ใบหลังขวา
        for (int i = 0; i < 4; i++)
        {
            SpawnNoble(tempNobles[i], i);
        }

        GameLog.Log("[NobleManager] สร้างและสุ่มขุนนาง 4 ใบเรียบร้อย");
    }

    // spawn ขุนนาง 1 ใบตามตำแหน่ง (index < 2 = ซ้าย, ที่เหลือขวา) — ใช้ทั้ง Setup และ SyncFromEntries
    private NobleDisplay SpawnNoble(NobleData selectedNoble, int index)
    {
        Transform targetContainer = (index < 2) ? leftContainer : rightContainer;
        if (targetContainer == null)
        {
            Debug.LogWarning("[NobleManager] ยังไม่ได้ผูก Left/Right Noble Container!");
            return null;
        }

        GameObject nobleObj = Object.Instantiate(noblePrefab, targetContainer);
        NobleDisplay display = nobleObj.GetComponent<NobleDisplay>();
        if (display != null)
        {
            display.SetupNoble(selectedNoble);
            active.Add(display);
            spawned.Add(display);
        }
        return display;
    }

    /// <summary>เช็คว่า player มีโบนัสครบเงื่อนไขขุนนางใบไหน → ให้คะแนน + เอาออกจาก active</summary>
    /// <param name="seat">index ที่นั่งของผู้เล่น (สำหรับ log→DB); -1 = ไม่ทราบ</param>
    public void CheckClaim(PlayerUI player, int seat = -1)
    {
        if (player == null) return;

        // เช็คย้อนกลับ เพราะอาจลบออกจาก active ระหว่าง loop
        for (int i = active.Count - 1; i >= 0; i--)
        {
            NobleDisplay nobleDisplay = active[i];
            NobleData data = nobleDisplay.nobleData;

            bool canClaim = true;
            for (int b = 0; b < 5; b++)
            {
                if (player.bonuses[b] < data.requiredBonuses[b])
                {
                    canClaim = false;
                    break;
                }
            }

            if (canClaim)
            {
                string claimerName = player.nameText != null ? player.nameText.text : "ผู้เล่น";
                GameLog.Log($"[Noble] {claimerName} ได้รับขุนนาง: {data.nobleName} (+{data.victoryPoints} VP)");

                // [Log→DB] บันทึกการได้ขุนนาง — VP จากขุนนางหายจาก log มาตลอด ทำให้ reconstruct คะแนนไม่ตรง game_end.scores
                GameLogger.Log("claim_noble", new GameLogger.Payload()
                    .Add("seat", seat)
                    .Add("nobleId", data.nobleName)
                    .Add("vp", data.victoryPoints)
                    .Add("isBot", player.isBot));

                player.AddScore(data.victoryPoints);
                nobleDisplay.ClaimNoble(claimerName);
                active.RemoveAt(i);
                claimedBy[data.nobleName] = claimerName; // [Noble sync] จำไว้ส่งให้เครื่องอื่นเห็นว่าใครได้ใบนี้
            }
        }
    }

    /// <summary>
    /// ซ่อน (mark claimed) ขุนนางตามชื่อ — ใช้ตอน render-from-state (core/host เป็นคน claim)
    /// ต่างจาก CheckClaim: **ไม่บวกคะแนน** (คะแนนถูก render จาก PlayerState แล้ว) แค่อัปเดต visual + เอาออกจาก active
    /// idempotent: ถ้าใบนั้นไม่อยู่ใน active แล้ว (เคย claim ไป) → no-op ปลอดภัยเมื่อเรียกซ้ำ
    /// คืน true ถ้าเพิ่งซ่อนใบนี้, false ถ้าไม่พบ/เคยซ่อนแล้ว
    /// </summary>
    public bool ClaimByName(string nobleName, string claimerName)
    {
        if (string.IsNullOrEmpty(nobleName)) return false;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            NobleDisplay d = active[i];
            if (d != null && d.nobleData != null && d.nobleData.nobleName == nobleName)
            {
                d.ClaimNoble(claimerName);
                active.RemoveAt(i);
                claimedBy[nobleName] = claimerName; // [Noble sync] จำไว้ส่งต่อ
                return true;
            }
        }
        return false;
    }

    // ============================================================
    // [Noble sync — online] ขุนนางต้องเหมือนกันทุกเครื่อง (เดิมแต่ละเครื่องสุ่มเอง + claim ไม่ส่งข้ามเครื่อง)
    //   BuildSyncEntries: ฝั่งส่ง (ไปกับ BoardStateSnapshot) — entry ละใบ "ชื่อ~คนclaim" (ว่าง = ยังไม่ถูก claim)
    //   SyncFromEntries:  ฝั่งรับ — ชุดชื่อไม่ตรง → เคลียร์แล้ว spawn ใหม่ตามผู้ส่ง; ใบที่ถูก claim → ซ่อน visual
    //                     (ไม่บวกคะแนน — คะแนนมากับ economy snapshot อยู่แล้ว) — idempotent เรียกซ้ำได้
    // ============================================================

    public string[] BuildSyncEntries()
    {
        var entries = new string[spawned.Count];
        for (int i = 0; i < spawned.Count; i++)
        {
            NobleDisplay d = spawned[i];
            string name = (d != null && d.nobleData != null) ? d.nobleData.nobleName : "";
            claimedBy.TryGetValue(name, out string claimer);
            entries[i] = $"{name}~{claimer ?? ""}";
        }
        return entries;
    }

    public void SyncFromEntries(string[] entries)
    {
        if (entries == null || entries.Length == 0) return; // ผู้ส่งเป็น build เก่า/ยังไม่ setup → อย่าแตะของเดิม

        // แตก entry เป็น (ชื่อ, คนclaim)
        var names = new List<string>();
        var claimers = new List<string>();
        foreach (string e in entries)
        {
            if (string.IsNullOrEmpty(e)) continue;
            int sep = e.IndexOf('~');
            names.Add(sep >= 0 ? e.Substring(0, sep) : e);
            claimers.Add(sep >= 0 ? e.Substring(sep + 1) : "");
        }
        if (names.Count == 0) return;

        // ชุดขุนนางในเครื่องตรงกับผู้ส่งไหม (ชื่อ+ลำดับ) — ไม่ตรง (เช่นเครื่องนี้สุ่มเองตอนเริ่ม) → สร้างใหม่ตามผู้ส่ง
        bool sameSet = spawned.Count == names.Count;
        for (int i = 0; sameSet && i < spawned.Count; i++)
        {
            string localName = (spawned[i] != null && spawned[i].nobleData != null) ? spawned[i].nobleData.nobleName : "";
            if (localName != names[i]) sameSet = false;
        }

        if (!sameSet)
        {
            GameLog.Log("[NobleManager] ชุดขุนนางไม่ตรงกับผู้ส่ง → สร้างใหม่ตาม snapshot");
            foreach (var d in spawned) if (d != null) Object.Destroy(d.gameObject);
            active.Clear();
            spawned.Clear();
            claimedBy.Clear();

            for (int i = 0; i < names.Count; i++)
            {
                NobleData data = masterPool != null ? masterPool.Find(n => n != null && n.nobleName == names[i]) : null;
                if (data == null)
                {
                    Debug.LogWarning($"[NobleManager] ไม่พบขุนนาง '{names[i]}' ใน master pool — ข้ามใบนี้");
                    continue;
                }
                SpawnNoble(data, i);
            }
        }

        // apply สถานะ claim (ClaimByName idempotent — ใบที่ซ่อนไปแล้วเรียกซ้ำ = no-op)
        for (int i = 0; i < names.Count; i++)
        {
            if (!string.IsNullOrEmpty(claimers[i]))
            {
                ClaimByName(names[i], claimers[i]);
            }
        }
    }
}
