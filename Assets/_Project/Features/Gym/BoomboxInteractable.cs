using System;
using System.Collections.Generic;
using Dormitory7.Audio;
using Dormitory7.Core;
using Dormitory7.Interaction;
using Dormitory7.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Dormitory7.Features.Gym
{
    [Serializable]
    public struct BoomboxTrack
    {
        public string title;
        public AudioClip clip;
    }

    public class BoomboxInteractable : MonoBehaviour, IInteractable
    {
        [SerializeField] private List<BoomboxTrack> tracks = new List<BoomboxTrack>();
        [SerializeField] private int currentTrackIndex;

        private bool _isMenuOpen;
        private PlayerContext _ctx;
        private GameObject _menuCanvasGo;
        private readonly List<Image> _rowBackgrounds = new List<Image>();

        private static readonly Color PanelBg = new Color(0.08f, 0.08f, 0.11f, 0.94f);
        private static readonly Color RowNormal = new Color(0.16f, 0.16f, 0.22f, 0.95f);
        private static readonly Color RowActive = new Color(0.62f, 0.22f, 0.45f, 0.98f);

        private void Awake()
        {
            if (tracks.Count == 0)
            {
                tracks.Add(new BoomboxTrack { title = "Стандартний фонк", clip = null });
            }
        }

        private void OnDestroy()
        {
            if (_isMenuOpen)
                CloseMenu();
            if (_menuCanvasGo != null)
                Destroy(_menuCanvasGo);
        }

        public string GetPrompt(PlayerContext context)
        {
            if (_isMenuOpen) return string.Empty;
            string currentTitle = GetTrackTitle(currentTrackIndex);
            return $"[E] Бумбокс: {currentTitle} (обрати трек)";
        }

        public bool CanInteract(PlayerContext context)
        {
            return !_isMenuOpen && tracks.Count > 0;
        }

        public void Interact(PlayerContext context)
        {
            if (!CanInteract(context)) return;
            OpenMenu(context);
        }

        private string GetTrackTitle(int index)
        {
            if (index < 0 || index >= tracks.Count) return "Стандартний фонк";
            return string.IsNullOrWhiteSpace(tracks[index].title) ? $"Трек {index + 1}" : tracks[index].title;
        }

        private void OpenMenu(PlayerContext context)
        {
            _ctx = context;
            _isMenuOpen = true;

            _ctx.Controller?.SetMovementLocked(true);
            _ctx.Inventory?.SetInputLocked(true);
            GameStateManager.Instance?.RegisterActiveQte(CloseMenu);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            EnsureMenuUi();
            RefreshRowHighlights();
            _menuCanvasGo.SetActive(true);
        }

        private void CloseMenu()
        {
            if (!_isMenuOpen) return;
            _isMenuOpen = false;
            GameStateManager.Instance?.UnregisterActiveQte(CloseMenu);

            _ctx.Controller?.SetMovementLocked(false);
            _ctx.Inventory?.SetInputLocked(false);

            bool isPlaying = GameStateManager.Instance == null || GameStateManager.Instance.CurrentState == GameState.Playing;
            Cursor.lockState = isPlaying ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !isPlaying;

            if (_menuCanvasGo != null)
                _menuCanvasGo.SetActive(false);
        }

        private void Update()
        {
            if (!_isMenuOpen) return;
            if (GameStateManager.Instance != null && GameStateManager.Instance.CurrentState != GameState.Playing)
            {
                CloseMenu();
                return;
            }

            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            for (int i = 0; i < Mathf.Min(9, tracks.Count); i++)
            {
                Key key = Key.Digit1 + i;
                if (kb[key].wasPressedThisFrame)
                {
                    SelectTrack(i);
                    return;
                }
            }
        }

        public void SelectTrack(int index)
        {
            if (index < 0 || index >= tracks.Count) return;
            currentTrackIndex = index;

            BoomboxTrack track = tracks[index];
            AudioManager.Instance?.SetGymMusicClip(track.clip);
            HudController.Instance?.ShowToast($"Бумбокс: {GetTrackTitle(index)}");
            CloseMenu();
        }

        private void RefreshRowHighlights()
        {
            for (int i = 0; i < _rowBackgrounds.Count; i++)
            {
                if (_rowBackgrounds[i] != null)
                    _rowBackgrounds[i].color = i == currentTrackIndex ? RowActive : RowNormal;
            }
        }

        private void EnsureMenuUi()
        {
            if (_menuCanvasGo != null) return;

            TMP_FontAsset font = FindCyrillicFont();

            _menuCanvasGo = new GameObject("BoomboxTrackPickerUI");
            Canvas canvas = _menuCanvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 120;

            CanvasScaler scaler = _menuCanvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            _menuCanvasGo.AddComponent<GraphicRaycaster>();

            GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(_menuCanvasGo.transform, false);
            Image panelImg = panel.GetComponent<Image>();
            panelImg.color = PanelBg;

            float height = 150f + tracks.Count * 62f;
            RectTransform panelRt = panel.GetComponent<RectTransform>();
            panelRt.sizeDelta = new Vector2(560f, height);
            panelRt.anchoredPosition = Vector2.zero;

            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 20, 20);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;

            CreateLabel(panel.transform, "БУМБОКС — ВИБІР ТРЕКУ", 26, font, TextAlignmentOptions.Center, 38f);

            _rowBackgrounds.Clear();
            for (int i = 0; i < tracks.Count; i++)
            {
                int capturedIndex = i;
                GameObject btnGo = new GameObject($"TrackBtn_{i}", typeof(RectTransform), typeof(Image), typeof(Button));
                btnGo.transform.SetParent(panel.transform, false);

                LayoutElement le = btnGo.AddComponent<LayoutElement>();
                le.preferredHeight = 52f;

                Image bg = btnGo.GetComponent<Image>();
                bg.color = RowNormal;
                _rowBackgrounds.Add(bg);

                Button btn = btnGo.GetComponent<Button>();
                btn.onClick.AddListener(() => SelectTrack(capturedIndex));

                CreateLabel(btnGo.transform, $"[{i + 1}]  {GetTrackTitle(i)}", 21, font, TextAlignmentOptions.MidlineLeft, 52f, 16f);
            }

            GameObject closeGo = new GameObject("CloseBtn", typeof(RectTransform), typeof(Image), typeof(Button));
            closeGo.transform.SetParent(panel.transform, false);
            LayoutElement closeLe = closeGo.AddComponent<LayoutElement>();
            closeLe.preferredHeight = 44f;
            closeGo.GetComponent<Image>().color = new Color(0.28f, 0.16f, 0.18f, 0.95f);
            closeGo.GetComponent<Button>().onClick.AddListener(CloseMenu);
            CreateLabel(closeGo.transform, "[Esc] Закрити", 19, font, TextAlignmentOptions.Center, 44f);
        }

        private static void CreateLabel(
            Transform parent,
            string text,
            float size,
            TMP_FontAsset font,
            TextAlignmentOptions align,
            float height,
            float leftPad = 0f)
        {
            GameObject go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(leftPad, 0f);
            rt.offsetMax = new Vector2(-leftPad, 0f);

            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.alignment = align;
            tmp.color = Color.white;
            if (font != null) tmp.font = font;
        }

        private static TMP_FontAsset FindCyrillicFont()
        {
            foreach (TMP_FontAsset f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (f != null && f.name.Contains("Cyrillic"))
                    return f;
            }
            return null;
        }
    }
}
