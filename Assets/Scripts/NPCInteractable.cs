using UnityEngine;

public class NPCInteractable : ProjectionInteractable
{
    [Header("Dialogue")]
    public string npcName = "npc-1";
    public string[] dialogueLines;
    public bool loopDialogue = true;
    public DialogueBoxUI dialogueBox;

    [Header("Animation")]
    public Animator npcAnimator;
    public string playerNearAnimationTrigger = "PlayerNear";
    public string interactAnimationTrigger = "Interact";

    int currentLineIndex;

    /*
     * Unity setup:
     * 1. Create an NPC GameObject and name it npc-1.
     * 2. Add a Renderer or Collider so ProjectionInteractable can calculate interaction bounds.
     * 3. Add NPCInteractable.
     * 4. If the NPC has ProjectionObjectRoleMarker, set role to Interactable.
     * 5. Add an Animator to npc-1.
     * 6. In the Animator Controller, add Trigger parameters named PlayerNear and Interact.
     * 7. Create a DialogueBox under your Canvas.
     * 8. Keep DialogueBox inactive by default.
     * 9. Add DialogueBoxUI to the DialogueBox or a nearby UI controller object.
     * 10. Drag the DialogueBoxUI component into NPCInteractable.dialogueBox.
     *
     * Animation clips are intentionally not hard-coded here. Adjust animation behavior in the
     * Animator Controller, and change trigger names in the Inspector when needed.
     */
    public override void ProjectionEnter(PlayerController player)
    {
        base.ProjectionEnter(player);
        SetAnimatorTrigger(playerNearAnimationTrigger);
    }

    public override void Interact(PlayerController player)
    {
        base.Interact(player);
        SetAnimatorTrigger(interactAnimationTrigger);
        ShowNextDialogueLine();
    }

    void ShowNextDialogueLine()
    {
        string line = GetCurrentDialogueLine();

        if (dialogueBox != null)
            dialogueBox.Show(npcName, line);

        AdvanceDialogueIndex();
    }

    string GetCurrentDialogueLine()
    {
        if (dialogueLines == null || dialogueLines.Length == 0)
            return "has no dialogue.";

        int safeIndex = Mathf.Clamp(currentLineIndex, 0, dialogueLines.Length - 1);
        return dialogueLines[safeIndex];
    }

    void AdvanceDialogueIndex()
    {
        if (dialogueLines == null || dialogueLines.Length == 0)
            return;

        if (currentLineIndex < dialogueLines.Length - 1)
        {
            currentLineIndex++;
            return;
        }

        if (loopDialogue)
            currentLineIndex = 0;
    }

    void SetAnimatorTrigger(string triggerName)
    {
        if (npcAnimator == null || string.IsNullOrEmpty(triggerName))
            return;

        npcAnimator.SetTrigger(triggerName);
    }
}
