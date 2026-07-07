using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class TutorialManager : MonoBehaviour
{
    public TutorialUIMask uiMask;
    public GameController gameController;
    
    [Header("Button References")]
    public Button endTurnButton;
    public Button clearButton;
    
    [HideInInspector]
    public bool skipRequested = false;
    
    public void RequestSkip() 
    { 
        skipRequested = true; 
    }
    
    private void Start()
    {
        // อำนวยความสะดวกเวลาทดสอบรันตรงๆ จาก Scene ใน Unity Editor
#if UNITY_EDITOR
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (sceneName == "TutorialScene" || sceneName == "TutorialScence")
        {
            PlayerPrefs.SetString("GameMode", "Tutorial");
        }
#endif

        // ตรวจสอบว่าเข้าโหมด Tutorial หรือไม่
        if(PlayerPrefs.GetString("GameMode") != "Tutorial") {
            Destroy(gameObject);
            return;
        }

        if (uiMask != null) uiMask.Hide();

        StartCoroutine(TutorialFlow());
    }

    private void OnDestroy()
    {
        // ป้องกันบัคเกมค้างถ้าสคริปต์โดนทำลาย
        Time.timeScale = 1f;
    }

    private IEnumerator TutorialFlow()
    {
        TutorialStepHandler handler = new TutorialStepHandler(this);
        yield return StartCoroutine(handler.ExecuteTutorial());
    }
}
