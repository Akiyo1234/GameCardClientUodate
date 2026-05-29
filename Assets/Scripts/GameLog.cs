using UnityEngine;

/// <summary>
/// Logger สำหรับ log ทั่วไป — ถูก "ตัดทิ้งอัตโนมัติ" ตอน build จริง (ที่ไม่มี UNITY_EDITOR)
/// ด้วยแอตทริบิวต์ [Conditional] คอมไพเลอร์จะลบทั้งการเรียกฟังก์ชันและการคำนวณ argument ออกให้เลย
/// → ไม่มี log รก ไม่กิน performance ตอนปล่อยเกม
///
/// ใช้แทน Debug.Log() เท่านั้น  ส่วน Debug.LogWarning / Debug.LogError ยังใช้ตรง ๆ
/// เพราะ warning/error ควรขึ้นใน build ด้วย
///
/// หมายเหตุ: log จะทำงานทั้งใน Editor และใน "Development Build" (ติ๊ก Development Build ตอน build)
/// เพื่อให้ดู flow ผ่าน adb logcat ตอนเทสบน Android ได้  ส่วน release build จริงยังถูกตัดทิ้งทั้งหมด
/// </summary>
public static class GameLog
{
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public static void Log(object message) => Debug.Log(message);

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public static void Log(object message, Object context) => Debug.Log(message, context);
}
