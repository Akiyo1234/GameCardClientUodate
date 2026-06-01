using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using Supabase;
using Supabase.Gotrue;
using System.Threading.Tasks;

// =============================================================================
// SupabaseManager — ระบบ Backend หลักของเกม
// -----------------------------------------------------------------------
// หน้าที่:
//   1. เชื่อมต่อกับ Supabase (ฐานข้อมูล Cloud) ตั้งแต่เปิดแอปครั้งแรก
//   2. จัดการ Authentication: ล็อกอิน / ออกจากระบบ
//   3. ดึงชื่อผู้เล่น (Username) จาก User Metadata
//   4. Rehydrate session อัตโนมัติเมื่อกลับจาก Background (Android/iOS)
// -----------------------------------------------------------------------
// Pattern: Singleton — มีแค่ 1 instance ตลอดชีวิตของแอป
//          DontDestroyOnLoad เพื่อให้ข้อมูล auth ยังอยู่ข้าม Scene
// =============================================================================
public class SupabaseManager : MonoBehaviour
{
    // Singleton instance — สคริปต์อื่นเรียกผ่าน SupabaseManager.Instance
    public static SupabaseManager Instance { get; private set; }

    [Header("Supabase Credentials (ได้จากหน้าเว็บ)")]
    [Tooltip("วาง URL ที่ก็อปปี้มาจาก Supabase ที่นี่")]
    public string supabaseUrl = "";
    
    [Tooltip("วาง Anon Key ที่ก็อปปี้มาจาก Supabase ที่นี่")]
    public string supabaseKey = "";

    // ตัวแปรสำหรับเรียกใช้ฐานข้อมูลจากสคริปต์อื่น
    private Supabase.Client supabaseClient;
    public Supabase.Client Client => supabaseClient;
    public bool IsInitialized { get; private set; }
    public string SupabaseUrl => supabaseUrl;
    public string SupabaseAnonKey => supabaseKey;

    private async void Awake()
    {
        // ── Singleton Guard ──
        // ถ้ามี Instance อยู่แล้ว (เช่น โหลดซ้ำจาก Scene อื่น) ให้ลบตัวใหม่ทิ้ง
        // เพื่อไม่ให้มี SupabaseManager ซ้อนกัน 2 ตัว
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject); // ทำให้ object นี้ไม่ถูกลบเมื่อเปลี่ยน Scene

        // ── เริ่มต้นเชื่อมต่อ Supabase ทันทีตอน Awake ──
        // ใช้ try/catch ครอบ async void เพราะถ้า exception หลุดออกมาใน async void
        // Unity จะไม่ catch ได้ และแอปจะค้างโดยไม่มี error log
        try
        {
            await InitializeSupabase();
        }
        catch (Exception e)
        {
            Debug.LogError($"<color=red>❌ [Supabase] เริ่มต้นการเชื่อมต่อล้มเหลว: {e.Message}</color>\n{e}");
        }
    }

    // ── ขั้นตอนการเชื่อมต่อ Supabase ──
    // 1. ตรวจว่ามี URL และ Key หรือยัง (ถ้าไม่ได้กรอกใน Inspector → ดึงจาก SupabaseConfig)
    // 2. สร้าง Supabase.Client พร้อม AutoConnectRealtime
    // 3. ถ้ามี session เก่า (Auto Login) → โหลด Profile จาก DB ทันที
    private async Task InitializeSupabase()
    {
        IsInitialized = false;

        // ถ้าไม่ได้กรอกใน Inspector ให้ดึงค่าจาก SupabaseConfig (แหล่งเดียว ไม่ hardcode)
        if (string.IsNullOrEmpty(supabaseUrl)) supabaseUrl = SupabaseConfig.Url;
        if (string.IsNullOrEmpty(supabaseKey)) supabaseKey = SupabaseConfig.AnonKey;

        if (string.IsNullOrEmpty(supabaseUrl) || string.IsNullOrEmpty(supabaseKey))
        {
            Debug.LogError("<color=red>❌ [Supabase] ล้มเหลว! คุณยังไม่ได้ใส่ URL หรือ Key ใน Inspector</color>");
            return;
        }

        var options = new SupabaseOptions
        {
            AutoConnectRealtime = true
        };

        // สร้างตัวเชื่อมต่อ
        supabaseClient = new Supabase.Client(supabaseUrl, supabaseKey, options);
        await supabaseClient.InitializeAsync();
        IsInitialized = true;
        
        GameLog.Log("<color=green>✅ [Supabase] เชื่อมต่อ Database สำเร็จแล้ว!</color>");

        // ถ้ามี Session เก่าอยู่แล้ว (Auto Login) ให้โหลด Profile ทันที
        if (supabaseClient.Auth.CurrentUser != null)
        {
            GameLog.Log($"[Supabase] Active session found for: {supabaseClient.Auth.CurrentUser.Email}. Loading profile...");
            await PlayerDataService.LoadProfileAsync();
        }
    }

    // หมายเหตุ: การสมัครสมาชิกย้ายไปทำที่หน้าเว็บ (StreamingAssets/Web/index.html)
    // ผ่านระบบ OTP (Edge Functions send-otp / verify-otp) แล้ว จึงไม่มี SignUpUser ในเกมอีกต่อไป

    // ── ระบบ Login ──
    // รับ email + password → เรียก Supabase Auth.SignIn
    // ถ้าสำเร็จ → โหลด PlayerProfile (Gems, MMR) จาก Database ทันที
    // ส่งกลับ (true, "") ถ้าสำเร็จ หรือ (false, errorMsg) ถ้าล้มเหลว
    public async Task<(bool success, string errorMsg)> SignInUser(string email, string password)
    {
        try
        {
            // ตัด whitespace ป้องกันการ login ผิดพลาดจากช่องว่างหน้า-หลัง
            email = email.Trim();
            password = password.Trim();
            
            var session = await supabaseClient.Auth.SignIn(email, password);
            if (session != null && session.User != null)
            {
                GameLog.Log($"<color=green>✅ [Supabase] ล็อกอินสำเร็จ ยินดีต้อนรับ: {session.User.Email}</color>");
                
                // โหลดข้อมูลผู้เล่น (Gems, MMR) จาก Database ทันทีที่ล็อกอิน
                await PlayerDataService.LoadProfileAsync();
                
                return (true, "");
            }
        }
        catch (System.Exception e)
        {
            // [FIX] ใช้ LogWarning แทน LogError สำหรับกรณีรหัสผิด
            // เพื่อป้องกัน Unity Editor เปิดฟีเจอร์ "Error Pause" แล้วทำให้เกมหยุดค้าง
            Debug.LogWarning($"<color=orange>⚠️ [Supabase] ล็อกอินไม่สำเร็จ: {e.Message}</color>");
            return (false, e.Message);
        }
        return (false, "เกิดข้อผิดพลาดไม่ทราบสาเหตุ");
    }

    // ── ดึงชื่อผู้เล่นที่ login อยู่ ──
    // ดึงจาก UserMetadata (ฝั่ง Supabase Auth) ถ้ามี field "username" → ใช้นั้น
    // ถ้าไม่มี → ใช้ Email แทน  ถ้ายัง login ไม่เข้า → ใช้ "Player 1" เป็น fallback
    public string GetCurrentUsername()
    {
        if (supabaseClient?.Auth.CurrentUser != null)
        {
            var user = supabaseClient.Auth.CurrentUser;
            if (user.UserMetadata != null && user.UserMetadata.ContainsKey("username"))
            {
                return user.UserMetadata["username"].ToString();
            }
            return user.Email; // ถ้าไม่มี username ให้ใช้อีเมลแก้ขัด
        }
        return "Player 1";
    }

    // หมายเหตุ: เดิมเคยมี CreateRoom() ที่ insert ลง public.rooms ตรงจาก client
    // ย้ายไป Edge Function แล้ว → ใช้ PlayerDataService.CreateRoomAsync() แทน
    // (server-authoritative, ใช้ service_role bypass RLS rooms_public_read)

    // ── ออกจากระบบ (Logout) ──
    // เรียก Supabase SignOut → ลบ key ที่เกี่ยวกับ auth + player data จาก PlayerPrefs
    // ไม่ DeleteAll() เพราะจะลบ settings อื่นที่ไม่เกี่ยวด้วยทิ้งโดยไม่ตั้งใจ
    public async Task SignOut()
    {
        if (supabaseClient?.Auth != null)
        {
            await supabaseClient.Auth.SignOut();
            GameLog.Log("<color=orange>⚠️ [Supabase] ออกจากระบบแล้ว</color>");

            // ล้างเฉพาะ key ที่เกี่ยวกับ auth และ player data
            // ไม่ใช้ DeleteAll() เพราะจะลบ settings อื่นๆ ทิ้งโดยไม่ตั้งใจ
            PlayerPrefs.DeleteKey("Username");
            PlayerPrefs.DeleteKey("TotalGems");
            PlayerPrefs.DeleteKey("MMR");
            PlayerPrefs.DeleteKey("OwnedItems");
            PlayerPrefs.DeleteKey("EquippedFrame");
            PlayerPrefs.DeleteKey("SelectedCharacter");
            PlayerPrefs.DeleteKey("MatchmakingPlayerId");
            PlayerPrefs.DeleteKey("MatchmakingRoomId");
            PlayerPrefs.DeleteKey("MatchmakingRoomCode");
            PlayerPrefs.DeleteKey("MatchmakingTargetPlayerCount");
            PlayerPrefs.DeleteKey("GameMode");
            PlayerPrefs.Save();
        }
    }

    // ── จัดการ Session เมื่อแอปกลับมา Foreground (Android/iOS) ──
    // pauseStatus=true = แอปถูก pause (ไปข้างหลัง)
    // pauseStatus=false = แอปกลับมา Resume → ตรวจว่า session ยังใช้ได้ไหม
    private void OnApplicationPause(bool pauseStatus)
    {
        // เมื่อกลับเข้ามาที่แอป (Resume) บน Android/iOS
        if (!pauseStatus && IsInitialized && supabaseClient?.Auth != null)
        {
            _ = RehydrateSessionAsync(); // เช็คและฟื้น session ที่อาจหมดอายุ
        }
    }

    private async Task RehydrateSessionAsync()
    {
        try
        {
            var session = supabaseClient.Auth.CurrentSession;
            if (session == null || string.IsNullOrEmpty(session.AccessToken))
            {
                GameLog.Log("[Supabase] Re-hydrating session from PlayerPrefs...");
                await supabaseClient.Auth.RetrieveSessionAsync();
                GameLog.Log("[Supabase] Session re-hydrated successfully.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Supabase] Failed to re-hydrate session: {ex.Message}");
        }
    }
}
