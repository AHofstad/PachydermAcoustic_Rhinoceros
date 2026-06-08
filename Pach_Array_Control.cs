using Eto.Drawing;
using Eto.Forms;
using Pachyderm_Acoustic.Utilities;
using Rhino.DocObjects;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Pachyderm_Acoustic
{
    namespace UI
    {
        public class Pach_ArrayControl : Form
        {
            private readonly List<RhinoObject> Elements;

            private DynamicLayout ElementLayout;
            private Scrollable ElementScroll;

            private NumericStepper GroupAlt;
            private NumericStepper GroupAzi;
            private NumericStepper GroupAxial;
            private double[] CurrentGroupAim = new double[3];
            private bool LoadingGroup = false;

            private CheckBox ShowPattern;
            private DropDown PatternOctave;
            private NumericStepper PatternReferenceDistance;

            private SpeakerPatternConduit PatternConduit;

            public Pach_ArrayControl(List<RhinoObject> elements)
            {
                Elements = elements;

                Title = "Pachyderm Speaker Array Control";
                Width = 1050;
                Height = 750;

                PatternConduit = new SpeakerPatternConduit();
                PatternConduit.Mode = SpeakerPatternConduit.Display_Mode.Boundary_Contours;
                PatternConduit.Contour_Levels = new double[] { -1, -2, -3, -4, -5, -6, -12, -18 };
                PatternConduit.Contour_Mesh_Max_Edge = 2.0;
                PatternConduit.Enabled = false;

                DynamicLayout layout = new DynamicLayout();
                layout.Padding = 8;
                layout.DefaultSpacing = new Size(6, 6);

                string label = "";
                if (Elements == null || Elements.Count == 0) label = "Steerable Array " + Elements[0].Geometry.GetUserString("ArrayGroupLabel");

                layout.AddRow(new Label { Text = label, Font = new Eto.Drawing.Font(SystemFont.Bold, 12)});

                //Add Pattern Controls
                GroupBox box = new GroupBox { Text = "Resulting Array Pattern" };

                DynamicLayout l = new DynamicLayout();
                l.Padding = 8;
                l.DefaultSpacing = new Size(4, 6);

                ShowPattern = new CheckBox { Text = "Show resulting array contours on walls" };
                ShowPattern.Checked = true;
                ShowPattern.CheckedChanged += (s, e) => UpdatePatternConduit();

                PatternOctave = new DropDown();
                PatternOctave.Items.Add("63 Hz.");
                PatternOctave.Items.Add("125 Hz.");
                PatternOctave.Items.Add("250 Hz.");
                PatternOctave.Items.Add("500 Hz.");
                PatternOctave.Items.Add("1 kHz.");
                PatternOctave.Items.Add("2 kHz.");
                PatternOctave.Items.Add("4 kHz.");
                PatternOctave.Items.Add("8 kHz.");
                PatternOctave.SelectedIndex = 4;
                PatternOctave.SelectedIndexChanged += (s, e) => UpdatePatternConduit();

                PatternReferenceDistance = new NumericStepper();
                PatternReferenceDistance.MinValue = 1;
                PatternReferenceDistance.MaxValue = 200;
                PatternReferenceDistance.DecimalPlaces = 2;
                PatternReferenceDistance.Value = 10;
                PatternReferenceDistance.ValueChanged += (s, e) => UpdatePatternConduit();

                l.AddRow(
                    ShowPattern,
                    new Label { Text = "Octave" },
                    PatternOctave,
                    new Label { Text = "Reference distance" },
                    PatternReferenceDistance);

                box.Content = l;
                layout.AddRow(box);



                //Add Group Aiming Controls
                GroupBox Gbox = new GroupBox { Text = "Group Aiming" };

                DynamicLayout Gl = new DynamicLayout();
                Gl.Padding = 8;
                Gl.DefaultSpacing = new Size(4, 6);

                GroupAlt = NewAngleStepper(-90, 90);
                GroupAzi = NewAngleStepper(-360, 360);
                GroupAxial = NewAngleStepper(-360, 360);

                GroupAlt.ValueChanged += GroupAiming_ValueChanged;
                GroupAzi.ValueChanged += GroupAiming_ValueChanged;
                GroupAxial.ValueChanged += GroupAiming_ValueChanged;

                Gl.AddRow(
                    new Label { Text = "Altitude" }, GroupAlt,
                    new Label { Text = "Azimuth" }, GroupAzi,
                    new Label { Text = "Axial" }, GroupAxial);

                Gbox.Content = Gl;
                layout.AddRow(Gbox);


                AddElementControls(layout);

                Button close = new Button { Text = "Close" };
                close.Click += (s, e) => Close();

                layout.AddRow(null, close);

                Content = layout;

                Closed += (s, e) =>
                {
                    if (PatternConduit != null)
                    {
                        PatternConduit.Clear();
                        PatternConduit.Enabled = false;
                    }
                };

                //Load Group Aiming
                LoadingGroup = true;

                double[] aim = null;

                if (Elements != null && Elements.Count > 0)
                {
                    string groupAim = Elements[0].Geometry.GetUserString("ArrayGroupAiming");

                    if (!string.IsNullOrWhiteSpace(groupAim))
                    {
                        aim = DecodeTriple(groupAim);
                    }
                    else
                    {
                        aim = DecodeTriple(Elements[0].Geometry.GetUserString("Aiming"));
                    }
                }

                if (aim == null) aim = new double[3];

                CurrentGroupAim = aim;

                GroupAlt.Value = aim[0];
                GroupAzi.Value = aim[1];
                GroupAxial.Value = aim[2];

                LoadingGroup = false;


                UpdatePatternConduit();
            }

            private void AddElementControls(DynamicLayout layout)
            {
                ElementScroll = new Scrollable();

                RebuildElementEditors();

                layout.AddRow(ElementScroll);
            }

            private NumericStepper NewAngleStepper(double min, double max)
            {
                return new NumericStepper
                {
                    DecimalPlaces = 3,
                    MinValue = min,
                    MaxValue = max,
                    Increment = 1,
                    Width = 90
                };
            }

            private void GroupAiming_ValueChanged(object sender, EventArgs e)
            {
                if (LoadingGroup) return;
                if (Elements == null || Elements.Count == 0) return;

                double[] newAim = new double[]
                {
                    GroupAlt.Value,
                    GroupAzi.Value,
                    GroupAxial.Value
                };

                double[] delta = new double[]
                {
                    newAim[0] - CurrentGroupAim[0],
                    newAim[1] - CurrentGroupAim[1],
                    newAim[2] - CurrentGroupAim[2]
                };

                for (int i = 0; i < Elements.Count; i++)
                {
                    RhinoObject obj = Elements[i];
                    if (obj == null || obj.Geometry == null) continue;

                    double[] elementAim = DecodeTriple(obj.Geometry.GetUserString("Aiming"));

                    elementAim[0] = Math.Min(Math.Max (elementAim[0] + delta[0], -90), 90);

                    double ang = elementAim[1] + delta[1];
                    elementAim[1] = (ang < 0 ? ang + 360 : ang) >= 360 ? ang - 360 : ang;
                    ang = elementAim[2] + delta[2];
                    elementAim[2] = (ang < 0 ? ang + 360 : ang) >= 360 ? ang - 360 : ang;

                    obj.Geometry.SetUserString("Aiming", EncodeTriple(elementAim));
                    obj.Geometry.SetUserString("ArrayGroupAiming", EncodeTriple(newAim));

                    EnsureSourceInConduit(obj);
                    Rhino.RhinoDoc.ActiveDoc.Views.Redraw();
                }

                CurrentGroupAim = newAim;

                RebuildElementEditors();
                UpdatePatternConduit();

                Rhino.RhinoDoc.ActiveDoc.Views.Redraw();
            }

            private static void EnsureSourceInConduit(Rhino.DocObjects.RhinoObject obj)
            {
                if (obj == null) return;

                bool found = false;

                foreach (System.Guid id in SourceConduit.Instance.UUID)
                {
                    if (id == obj.Id)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    SourceConduit.Instance.SetSource(obj);
                }
            }

            private void RebuildElementEditors()
            {
                ElementLayout = new DynamicLayout();
                ElementLayout.Padding = 4;
                ElementLayout.DefaultSpacing = new Eto.Drawing.Size(4, 8);

                for (int i = 0; i < Elements.Count; i++)
                {
                    ElementLayout.AddRow(new ArrayElementEditor(Elements[i], OnElementChanged));
                }

                ElementScroll.Content = ElementLayout;
            }

            private void OnElementChanged(RhinoObject obj)
            {
                if (obj == null) return;

                EnsureSourceInConduit(obj);
                Rhino.RhinoDoc.ActiveDoc.Views.Redraw();

                UpdatePatternConduit();

                Rhino.RhinoDoc.ActiveDoc.Views.Redraw();
            }

            private void UpdatePatternConduit()
            {
                if (PatternConduit == null) return;

                if (ShowPattern == null ||
                    !ShowPattern.Checked.HasValue ||
                    !ShowPattern.Checked.Value)
                {
                    PatternConduit.Clear();
                    return;
                }

                // Requires SetArraySources(...) added to SpeakerPatternConduit.
                PatternConduit.Octave = PatternOctave.SelectedIndex;
                PatternConduit.SetArrayElements(Elements, PatternReferenceDistance.Value);
                
            }

            internal static double[] DecodeTriple(string code)
            {
                double[] values = new double[3];

                if (string.IsNullOrWhiteSpace(code)) return values;

                string[] parts = code.Split(';');

                for (int i = 0; i < Math.Min(3, parts.Length); i++)
                {
                    double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]);
                }

                return values;
            }

            internal static string EncodeTriple(double[] values)
            {
                if (values == null || values.Length < 3)
                    values = new double[3];

                return values[0].ToString(CultureInfo.InvariantCulture) + ";" +
                       values[1].ToString(CultureInfo.InvariantCulture) + ";" +
                       values[2].ToString(CultureInfo.InvariantCulture);
            }
        }

        public class ArrayElementEditor : GroupBox
        {
            private readonly Rhino.DocObjects.RhinoObject Obj;
            private readonly Action<Rhino.DocObjects.RhinoObject> Changed;

            private NumericStepper Alt;
            private NumericStepper Azi;
            private NumericStepper Axial;
            private NumericStepper[] OctPhaseDelay = new NumericStepper[8];

            private bool Loading = false;

            public ArrayElementEditor(Rhino.DocObjects.RhinoObject obj, Action<Rhino.DocObjects.RhinoObject> changed)
            {
                Obj = obj;
                Changed = changed;

                if (obj == null || obj.Geometry == null) Text = "Array Element";
                else
                {
                    string label = obj.Geometry.GetUserString("SourceLabel");
                    if (!string.IsNullOrWhiteSpace(label)) Text = label;
                    else
                    {
                        string group = obj.Geometry.GetUserString("ArrayGroupLabel");
                        string suffix = obj.Geometry.GetUserString("ArrayElementSuffix");

                        if (!string.IsNullOrWhiteSpace(group) && !string.IsNullOrWhiteSpace(suffix))
                        {
                            Text = group + suffix;
                        }
                        else
                        {
                            Text = obj.Id.ToString();
                        }
                    }
                }
                DynamicLayout layout = new DynamicLayout();
                layout.Padding = 8;
                layout.DefaultSpacing = new Eto.Drawing.Size(4, 6);

                Alt = NewAngleStepper(-90, 90);
                Azi = NewAngleStepper(-360, 360);
                Axial = NewAngleStepper(-360, 360);

                Alt.ValueChanged += ValueChanged;
                Azi.ValueChanged += ValueChanged;
                Axial.ValueChanged += ValueChanged;

                layout.AddRow(
                    new Label { Text = "Altitude" }, Alt,
                    new Label { Text = "Azimuth" }, Azi,
                    new Label { Text = "Axial" }, Axial);

                DynamicLayout delayTable = new DynamicLayout();
                delayTable.DefaultSpacing = new Eto.Drawing.Size(4, 4);

                delayTable.AddRow(
                    new Label { Text = "63" },
                    new Label { Text = "125" },
                    new Label { Text = "250" },
                    new Label { Text = "500" },
                    new Label { Text = "1k" },
                    new Label { Text = "2k" },
                    new Label { Text = "4k" },
                    new Label { Text = "8k" });

                for (int i = 0; i < 8; i++)
                {
                    OctPhaseDelay[i] = new NumericStepper();
                    OctPhaseDelay[i].DecimalPlaces = 2;
                    OctPhaseDelay[i].MinValue = -720;
                    OctPhaseDelay[i].MaxValue = 720;
                    OctPhaseDelay[i].Increment = 5;
                    OctPhaseDelay[i].Width = 80;
                    OctPhaseDelay[i].ValueChanged += ValueChanged;
                }

                delayTable.AddRow(
                    OctPhaseDelay[0], OctPhaseDelay[1], OctPhaseDelay[2], OctPhaseDelay[3],
                    OctPhaseDelay[4], OctPhaseDelay[5], OctPhaseDelay[6], OctPhaseDelay[7]);

                GroupBox delayBox = new GroupBox();
                delayBox.Text = "Octave Phase Lag (degrees)"; 
                delayBox.Content = delayTable;

                layout.AddRow(delayBox);

                Button zeroDelays = new Button();
                zeroDelays.Text = "Zero Phase";
                zeroDelays.Click += (s, e) =>
                {
                    for (int i = 0; i < 8; i++)
                    {
                        OctPhaseDelay[i].Value = 0;
                    }

                    Commit();
                };

                layout.AddRow(null, zeroDelays);

                Content = layout;

                //Load From Object;
                Loading = true;

                double[] aim = PachTools.DecodeTriple(Obj.Geometry.GetUserString("Aiming"));

                Alt.Value = aim[0];
                Azi.Value = aim[1];
                Axial.Value = aim[2];

                double[] phase = PachTools.DecodeEight(Obj.Geometry.GetUserString("ArrayDelayOctaveMs"));

                for (int i = 0; i < 8; i++)
                {
                    OctPhaseDelay[i].Value = phase[i];
                }

                Loading = false;
            }

            private NumericStepper NewAngleStepper(double min, double max)
            {
                NumericStepper s = new NumericStepper();
                s.DecimalPlaces = 3;
                s.MinValue = min;
                s.MaxValue = max;
                s.Increment = 1;
                s.Width = 90;
                return s;
            }

            private void ValueChanged(object sender, EventArgs e)
            {
                if (Loading) return;
                Commit();
            }

            private void Commit()
            {
                Obj.Geometry.SetUserString(
                    "Aiming",
                    Alt.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" +
                    Azi.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" +
                    Axial.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));

                double[] phase = new double[8];
                double[] delay = new double[8];

                for (int i = 0; i < 8; i++)
                {
                    phase[i] = OctPhaseDelay[i].Value;
                    delay[i] = phase[i] / 360.0 / (62.5 * Math.Pow(2,i)) * 1000.0; ;
                }

                Obj.Geometry.SetUserString(
                    "ArrayPhaseOctaveDeg",
                    PachTools.EncodeEight(phase));

                Obj.Geometry.SetUserString(
                    "ArrayDelayOctaveMs",
                    PachTools.EncodeEight(delay));

                //Ensure the source is in the conduit
                bool found = false;

                foreach (System.Guid id in SourceConduit.Instance.UUID)
                {
                    if (id == Obj.Id)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found) SourceConduit.Instance.SetSource(Obj);

                Changed?.Invoke(Obj);
            }
        }
    }
}