using UnityEngine.UIElements;

namespace Sharq.Core
{
    /// <summary>
    /// Icon primitive. Renders a Phosphor VectorImage resolved by
    /// <see cref="SusIconRegistry"/> and painted as a background image.
    ///
    /// Reactive: change <see cref="Name"/> or <see cref="Weight"/> and the
    /// background image updates automatically.
    ///
    /// Usage:
    /// <code>
    /// var icon = new SusIconElement("gear");                     // regular by default
    /// var star = new SusIconElement("star", SusIconWeight.Fill);
    /// icon.Name.Value = "x";                               // reactive swap
    /// </code>
    /// </summary>
    public class SusIconElement : VisualElement
    {
        /// <summary>Icon alias (see <see cref="SusIconRegistry"/>). Reactive.</summary>
        public Prop<string> Name { get; }

        /// <summary>Phosphor weight variant. Reactive.</summary>
        public Prop<SusIconWeight> Weight { get; }

        public SusIconElement(string name = null, SusIconWeight weight = SusIconWeight.Regular)
        {
            AddToClassList("sus-icon-bg");

            Name = new Prop<string>(name);
            Weight = new Prop<SusIconWeight>(weight);

            Name.Changed += (_, __) => Render();
            Weight.Changed += (_, __) => Render();

            Render();
        }

        private void Render()
        {
            var alias = Name.Value;
            if (string.IsNullOrEmpty(alias))
            {
                this.style.backgroundImage = StyleKeyword.None;
                return;
            }

            var vec = SusIconRegistry.Load(alias, Weight.Value);
            this.style.backgroundImage = vec != null
                ? new StyleBackground(vec)
                : StyleKeyword.None;
        }
    }
}
