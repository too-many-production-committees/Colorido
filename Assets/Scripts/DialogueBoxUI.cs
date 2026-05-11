using UnityEngine;
using UnityEngine.UI;

#if TMP_PRESENT
using TMPro;
#endif

public class DialogueBoxUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject root;
    public Text dialogueText;

#if TMP_PRESENT
    public TMP_Text tmpDialogueText;
#else
    public Object tmpDialogueText;
#endif

    /*
     * Unity setup:
     * 1. Create a DialogueBox under your Canvas.
     * 2. Keep DialogueBox inactive by default.
     * 3. Add DialogueBoxUI to the DialogueBox or a nearby UI controller object.
     * 4. Assign root to the DialogueBox GameObject.
     * 5. Assign either a UnityEngine.UI.Text field, or a TMP_Text field if TextMeshPro is installed.
     * 6. Drag this DialogueBoxUI into NPCInteractable.dialogueBox.
     */
    public void Show(string speakerName, string line)
    {
        if (root != null)
            root.SetActive(true);

        string text = $"{speakerName}: {line}";

        if (dialogueText != null)
            dialogueText.text = text;

#if TMP_PRESENT
        if (tmpDialogueText != null)
            tmpDialogueText.text = text;
#endif
    }

    public void Hide()
    {
        if (root != null)
            root.SetActive(false);
    }
}
