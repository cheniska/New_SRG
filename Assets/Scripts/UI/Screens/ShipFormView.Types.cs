using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SRG.Combat;
using SRG.Config;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Presentation.Common;
using SRG.Ships;
using SRG.Controllers;
using SRG.UI.Common;
using SRG.UI.HUD;
using SRG.Utils;

namespace SRG.UI.Screens
{
    public partial class ShipFormView
    {
        // ── Внутренние типы ───────────────────────────────────────────────────────

        private enum SlotVisualState { Usual, MouseEntered, Blocked, Broken, Green }

        internal struct HandState
        {
            public ItemInstance Item;
            public SlotWidget OriginSlot;
        }

        internal class SlotWidget
        {
            public string SlotKey;
            public bool IsArtefactStyle;
            public bool IsCargo;
            public int CargoIndex;
            public Image Bg;
            public Image RaycastFallback;
            public Image Icon;
            public Image EmbedBadge;
            public SlotIconAnimator IconAnimator;
            public RectTransform Root;
            public ItemInstance Item;
            public bool IsHovered;
            public bool IsBlocked;
            public SlotTextureSet TextureSet;
        }

        internal class SlotTextureSet
        {
            public Sprite Usual, MouseEntered, Blocked, Broken, Green;
            public float Width = 96f;
            public float Height = 96f;
        }

        internal class SlotInputHandler : MonoBehaviour,
            IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
        {
            public ShipFormView Owner;
            public SlotWidget Widget;
            public void OnPointerEnter(PointerEventData _) { if (Owner != null) Owner.OnSlotEnter(Widget); }
            public void OnPointerExit(PointerEventData _)  { if (Owner != null) Owner.OnSlotExit(Widget);  }
            public void OnPointerClick(PointerEventData e) { if (Owner != null) Owner.OnSlotClick(Widget, e); }
        }

        internal class SlotIconAnimator : MonoBehaviour
        {
            public Image Target;
            public float Fps = 12f;
            private Sprite[] _frames;
            private int _frame;
            private float _timer;
            private bool _playing;

            public void Configure(Sprite[] frames)
            {
                _frames = frames;
                _frame = 0;
                _timer = 0f;
                _playing = false;
                Apply();
            }

            public void SetPlaying(bool playing)
            {
                if (_frames == null || _frames.Length <= 1) { _playing = false; return; }
                _playing = playing;
                // На паузе кадр НЕ сбрасываем — остаётся на текущем до следующего hover
                // или до полного перезахода в форму (Configure заново).
            }

            public void ResetFrame()
            {
                _frame = 0;
                _timer = 0f;
                _playing = false;
                Apply();
            }

            private void Update()
            {
                if (!_playing || _frames == null || _frames.Length <= 1) return;
                _timer += Time.unscaledDeltaTime;
                float secPerFrame = 1f / Fps;
                if (_timer < secPerFrame) return;
                int advance = Mathf.FloorToInt(_timer / secPerFrame);
                _timer %= secPerFrame;
                _frame = (_frame + advance) % _frames.Length;
                Apply();
            }

            private void Apply()
            {
                if (Target == null || _frames == null || _frames.Length == 0) return;
                int idx = Mathf.Clamp(_frame, 0, _frames.Length - 1);
                Target.sprite = _frames[idx];
            }
        }
    }
}
