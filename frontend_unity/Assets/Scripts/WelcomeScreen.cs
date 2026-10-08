using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class WelcomeScreen : MonoBehaviour
{
    [Header("References")]
    public ARNetworkManager networkManager;
    public TMP_InputField destinationField;

    [Header("Behavior")]
    [Tooltip("Keep this off when the Button already calls ARNetworkManager.OnClickStartNav directly.")]
    public bool triggerNavigationOnClick = false;

    [Header("Visual")]
    public bool applyRuntimeStyle = true;
    public Color glassColor = new Color(0.00f, 0.03f, 0.05f, 0.36f);
    public Color cyan = new Color(0.20f, 0.92f, 1.00f, 0.95f);
    public Color softWhite = new Color(0.92f, 0.98f, 1.00f, 0.96f);
    public Color mutedWhite = new Color(0.72f, 0.84f, 0.88f, 0.90f);

    [Header("Animation")]
    public bool animateHud = true;
    public float scanSpeed = 0.55f;
    public float pulseSpeed = 2.2f;

    private Image topScanLine;
    private Image buttonGlow;
    private Image inputGlow;
    private Image buttonImageRef;
    private RectTransform buttonRectRef;

    void Awake()
    {
        if (applyRuntimeStyle)
        {
            ApplyRuntimeStyle();
        }
    }

    void Update()
    {
        if (!animateHud || !applyRuntimeStyle)
        {
            return;
        }

        float pulse = (Mathf.Sin(Time.unscaledTime * pulseSpeed) + 1f) * 0.5f;

        if (buttonGlow != null)
        {
            buttonGlow.color = new Color(cyan.r, cyan.g, cyan.b, Mathf.Lerp(0.10f, 0.30f, pulse));
        }

        if (inputGlow != null)
        {
            inputGlow.color = new Color(cyan.r, cyan.g, cyan.b, Mathf.Lerp(0.06f, 0.16f, pulse));
        }

        if (buttonImageRef != null)
        {
            buttonImageRef.color = new Color(0.22f, Mathf.Lerp(0.78f, 0.94f, pulse), 1.00f, 0.96f);
        }

        if (buttonRectRef != null)
        {
            float scale = Mathf.Lerp(1.00f, 1.018f, pulse);
            buttonRectRef.localScale = new Vector3(scale, scale, 1f);
        }

        if (topScanLine != null)
        {
            float t = Mathf.Repeat(Time.unscaledTime * scanSpeed, 1f);
            float start = Mathf.Lerp(0.07f, 0.74f, t);
            float end = Mathf.Min(start + 0.19f, 0.93f);
            SetRect(topScanLine.rectTransform, new Vector2(start, 0.894f), new Vector2(end, 0.902f), Vector2.zero);
            topScanLine.color = new Color(cyan.r, cyan.g, cyan.b, Mathf.Lerp(0.35f, 0.88f, pulse));
        }
    }

    public void OnClickStart()
    {
        if (destinationField != null && string.IsNullOrEmpty(destinationField.text))
        {
            Debug.LogWarning("[WelcomeScreen] Destination is empty, ignore click.");
            return;
        }

        if (triggerNavigationOnClick && networkManager != null)
        {
            networkManager.OnClickStartNav();
        }
        else if (triggerNavigationOnClick)
        {
            Debug.LogError("[WelcomeScreen] networkManager is not assigned.");
        }

        gameObject.SetActive(false);
    }

    public void ShowCurtainAgain()
    {
        gameObject.SetActive(true);
    }

    private void ApplyRuntimeStyle()
    {
        StylePanel();
        StyleTopBadge();
        StyleTitle();
        StyleSubtitle();
        StyleHudDecorations();
        StyleInput();
        StyleButton();
        StyleHint();
    }

    private void StylePanel()
    {
        Image panelImage = GetComponent<Image>();
        if (panelImage != null)
        {
            panelImage.color = glassColor;
        }
    }

    private void StyleTopBadge()
    {
        TMP_Text topBadge = FindText("TopBadge");
        if (topBadge == null) return;

        topBadge.text = "STARTRACK  AR NAV";
        topBadge.fontSize = 28f;
        topBadge.fontStyle = FontStyles.Bold;
        topBadge.color = cyan;
        topBadge.characterSpacing = 10f;
        topBadge.alignment = TextAlignmentOptions.Left;
        SetRect(topBadge.rectTransform, new Vector2(0.07f, 0.91f), new Vector2(0.93f, 0.965f), Vector2.zero);
    }

    private void StyleTitle()
    {
        TMP_Text title = FindText("TitleText");
        if (title == null) return;

        title.text = "Begin Navigation";
        title.fontSize = 62f;
        title.fontStyle = FontStyles.Bold;
        title.color = softWhite;
        title.characterSpacing = 0f;
        title.alignment = TextAlignmentOptions.Center;
        SetRect(title.rectTransform, new Vector2(0.05f, 0.705f), new Vector2(0.95f, 0.805f), Vector2.zero);
    }

    private void StyleSubtitle()
    {
        TMP_Text subtitle = FindText("SubtitleText");
        if (subtitle == null) return;

        subtitle.text = "WiFi \u00B7 Vision \u00B7 Topology \u00B7 AR";
        subtitle.fontSize = 28f;
        subtitle.fontStyle = FontStyles.Bold;
        subtitle.color = mutedWhite;
        subtitle.alignment = TextAlignmentOptions.Center;
        subtitle.characterSpacing = 4f;
        SetRect(subtitle.rectTransform, new Vector2(0.08f, 0.655f), new Vector2(0.92f, 0.70f), Vector2.zero);
    }

    private void StyleInput()
    {
        TMP_Text inputLabel = FindText("inputLabel");
        if (inputLabel != null)
        {
            inputLabel.text = "Destination";
            inputLabel.fontSize = 28f;
            inputLabel.fontStyle = FontStyles.Bold;
            inputLabel.color = cyan;
            inputLabel.alignment = TextAlignmentOptions.Left;
            SetRect(inputLabel.rectTransform, new Vector2(0.09f, 0.492f), new Vector2(0.91f, 0.532f), Vector2.zero);
        }

        TMP_InputField input = destinationField != null ? destinationField : FindComponent<TMP_InputField>("InputField (TMP)");
        if (input == null) return;

        inputGlow = EnsureImage("HUD_InputGlow");
        inputGlow.color = new Color(cyan.r, cyan.g, cyan.b, 0.12f);
        SetRect(inputGlow.rectTransform, new Vector2(0.072f, 0.397f), new Vector2(0.928f, 0.483f), Vector2.zero);
        PutBehind(inputGlow.transform, input.transform);

        Image inputImage = input.GetComponent<Image>();
        if (inputImage != null)
        {
            inputImage.color = new Color(0.03f, 0.31f, 0.38f, 0.90f);
        }

        RectTransform inputRect = input.GetComponent<RectTransform>();
        SetRect(inputRect, new Vector2(0.08f, 0.405f), new Vector2(0.92f, 0.475f), Vector2.zero);
        AddBorder("HUD_InputBorder", new Vector2(0.08f, 0.405f), new Vector2(0.92f, 0.475f), new Color(cyan.r, cyan.g, cyan.b, 0.86f));

        if (input.textComponent != null)
        {
            input.textComponent.fontSize = 30f;
            input.textComponent.fontStyle = FontStyles.Normal;
            input.textComponent.color = softWhite;
            input.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
        }

        if (input.placeholder is TMP_Text placeholder)
        {
            placeholder.text = "Enter destination or intent";
            placeholder.fontSize = 29f;
            placeholder.fontStyle = FontStyles.Italic;
            placeholder.color = new Color(0.86f, 0.96f, 1.00f, 0.72f);
        }
    }

    private void StyleButton()
    {
        Button button = FindComponent<Button>("Button");
        if (button == null) return;

        buttonGlow = EnsureImage("HUD_ButtonGlow");
        buttonGlow.color = new Color(cyan.r, cyan.g, cyan.b, 0.16f);
        SetRect(buttonGlow.rectTransform, new Vector2(0.105f, 0.291f), new Vector2(0.895f, 0.379f), Vector2.zero);
        PutBehind(buttonGlow.transform, button.transform);

        Image buttonImage = button.GetComponent<Image>();
        buttonImageRef = buttonImage;
        if (buttonImageRef != null)
        {
            buttonImageRef.color = new Color(0.22f, 0.86f, 1.00f, 0.96f);
        }

        buttonRectRef = button.GetComponent<RectTransform>();
        SetRect(buttonRectRef, new Vector2(0.12f, 0.300f), new Vector2(0.88f, 0.370f), Vector2.zero);
        AddBorder("HUD_ButtonBorder", new Vector2(0.12f, 0.300f), new Vector2(0.88f, 0.370f), new Color(0.80f, 0.98f, 1.00f, 0.72f));

        TMP_Text buttonText = button.GetComponentInChildren<TMP_Text>(true);
        if (buttonText != null)
        {
            buttonText.text = "Start Navigation";
            buttonText.fontSize = 34f;
            buttonText.fontStyle = FontStyles.Bold;
            buttonText.color = new Color(0.02f, 0.08f, 0.10f, 0.92f);
            buttonText.alignment = TextAlignmentOptions.Center;
        }
    }

    private void StyleHint()
    {
        Image hintBox = FindComponent<Image>("HintBox");
        if (hintBox != null)
        {
            hintBox.color = new Color(0.00f, 0.02f, 0.03f, 0.34f);
            SetRect(hintBox.rectTransform, new Vector2(0.08f, 0.230f), new Vector2(0.92f, 0.270f), Vector2.zero);
        }

        TMP_Text hintText = FindText("HintBoxText");
        if (hintText == null) return;

        hintText.text = "Aim at the corridor before starting.";
        hintText.fontSize = 21f;
        hintText.fontStyle = FontStyles.Italic;
        hintText.color = new Color(0.90f, 0.98f, 1.00f, 0.92f);
        hintText.alignment = TextAlignmentOptions.Center;
        SetRect(hintText.rectTransform, new Vector2(0.10f, 0.235f), new Vector2(0.90f, 0.265f), Vector2.zero);
    }

    private void StyleHudDecorations()
    {
        RectTransform root = GetComponent<RectTransform>();
        if (root == null) return;

        Image titleBacking = EnsureImage("HUD_TitleBacking");
        titleBacking.color = new Color(0.00f, 0.03f, 0.05f, 0.32f);
        SetRect(titleBacking.rectTransform, new Vector2(0.04f, 0.635f), new Vector2(0.96f, 0.825f), Vector2.zero);
        titleBacking.transform.SetAsFirstSibling();

        Image controlDeck = EnsureImage("HUD_ControlDeck");
        controlDeck.color = new Color(0.00f, 0.06f, 0.08f, 0.62f);
        SetRect(controlDeck.rectTransform, new Vector2(0.045f, 0.205f), new Vector2(0.955f, 0.555f), Vector2.zero);
        controlDeck.transform.SetAsFirstSibling();

        AddLine("HUD_TopLine", new Vector2(0.07f, 0.895f), new Vector2(0.45f, 0.899f), new Color(cyan.r, cyan.g, cyan.b, 0.75f));

        topScanLine = EnsureImage("HUD_TopScanLine");
        topScanLine.color = new Color(cyan.r, cyan.g, cyan.b, 0.82f);
        SetRect(topScanLine.rectTransform, new Vector2(0.07f, 0.894f), new Vector2(0.25f, 0.902f), Vector2.zero);

        AddLine("HUD_TitleLine", new Vector2(0.26f, 0.635f), new Vector2(0.74f, 0.639f), new Color(cyan.r, cyan.g, cyan.b, 0.58f));
        AddLine("HUD_DeckLine", new Vector2(0.08f, 0.545f), new Vector2(0.92f, 0.549f), new Color(cyan.r, cyan.g, cyan.b, 0.48f));
    }

    private void AddLine(string name, Vector2 min, Vector2 max, Color color)
    {
        Image line = EnsureImage(name);
        line.color = color;
        SetRect(line.rectTransform, min, max, Vector2.zero);
    }

    private void AddBorder(string name, Vector2 min, Vector2 max, Color color)
    {
        const float thickness = 0.003f;
        AddLine(name + "_Top", new Vector2(min.x, max.y - thickness), new Vector2(max.x, max.y), color);
        AddLine(name + "_Bottom", new Vector2(min.x, min.y), new Vector2(max.x, min.y + thickness), color);
        AddLine(name + "_Left", new Vector2(min.x, min.y), new Vector2(min.x + thickness, max.y), color);
        AddLine(name + "_Right", new Vector2(max.x - thickness, min.y), new Vector2(max.x, max.y), color);
    }

    private void PutBehind(Transform decorative, Transform target)
    {
        if (decorative == null || target == null || decorative.parent != target.parent)
        {
            return;
        }

        decorative.SetSiblingIndex(Mathf.Max(0, target.GetSiblingIndex()));
    }

    private Image EnsureImage(string objectName)
    {
        Transform existing = FindDeep(transform, objectName);
        if (existing != null)
        {
            Image image = existing.GetComponent<Image>();
            if (image != null)
            {
                image.raycastTarget = false;
                return image;
            }
        }

        GameObject go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        Image newImage = go.AddComponent<Image>();
        newImage.raycastTarget = false;
        return newImage;
    }

    private TMP_Text FindText(string objectName)
    {
        Transform t = FindDeep(transform, objectName);
        return t != null ? t.GetComponent<TMP_Text>() : null;
    }

    private T FindComponent<T>(string objectName) where T : Component
    {
        Transform t = FindDeep(transform, objectName);
        return t != null ? t.GetComponent<T>() : null;
    }

    private Transform FindDeep(Transform root, string objectName)
    {
        if (root.name == objectName)
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), objectName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offset)
    {
        if (rect == null) return;

        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offset;
        rect.offsetMax = -offset;
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
    }
}
