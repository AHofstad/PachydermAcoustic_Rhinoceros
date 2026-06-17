using Eto.Drawing;
using Eto.Forms;
using Pachyderm_Acoustic.Utilities;
using Rhino.DocObjects;
using Rhino.Geometry;
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

            private bool LoadingGroup = false;

            private CheckBox ShowPattern;
            private DropDown PatternOctave;
            private NumericStepper PatternReferenceDistance;

            private SpeakerPatternConduit PatternConduit;

            public Pach_ArrayControl(List<RhinoObject> elements)
            {
                Elements = elements;

                Title = "Pachyderm Speaker Array Control";
                Width = 900;
                Height = 500;

                PatternConduit = SpeakerPatternConduit.Instance;
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

                Button aimAtPoints = new Button();
                aimAtPoints.Text = "Aim at Points";
                aimAtPoints.Click += (s, e) => AimPhaseAtPoints(false);

                Button optimizeAtPoints = new Button();
                optimizeAtPoints.Text = "Optimize";
                optimizeAtPoints.Click += (s, e) => AimPhaseAtPoints(true);

                l.AddRow(
                    ShowPattern,
                    new Label { Text = "Octave" },
                    PatternOctave,
                    new Label { Text = "Reference distance" },
                    PatternReferenceDistance,
                    aimAtPoints, optimizeAtPoints);

                box.Content = l;
                layout.AddRow(box);

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

                //double[] aim = null;

                //if (Elements != null && Elements.Count > 0)
                //{
                //    string groupAim = Elements[0].Geometry.GetUserString("ArrayGroupAiming");

                //    if (!string.IsNullOrWhiteSpace(groupAim))
                //    {
                //        aim = DecodeTriple(groupAim);
                //    }
                //    else
                //    {
                //        aim = DecodeTriple(Elements[0].Geometry.GetUserString("Aiming"));
                //    }
                //}

                //if (aim == null) aim = new double[3];

                LoadingGroup = false;
                UpdatePatternConduit();
            }

            private List<Rhino.Geometry.Point3d> GetTargetPoints()
            {
                List<Rhino.Geometry.Point3d> targets = new List<Rhino.Geometry.Point3d>();

                while (true)
                {
                    Rhino.Geometry.Point3d pt;
                    Rhino.Commands.Result rc = Rhino.Input.RhinoGet.GetPoint(
                        targets.Count == 0 ? "Select target point" : "Select another target point. Press Enter when done.",
                        true,
                        out pt);

                    if (rc == Rhino.Commands.Result.Success)
                    {
                        targets.Add(pt);
                        continue;
                    }

                    break;
                }

                return targets;
            }

            private void AddElementControls(DynamicLayout layout)
            {
                ElementScroll = new Scrollable();

                RebuildElementEditors();

                layout.AddRow(ElementScroll);
            }

            private void AimPhaseAtPoints(bool fineTune)
            {
                if (Elements == null || Elements.Count == 0) return;

                List<Rhino.Geometry.Point3d> targets = GetTargetPoints();
                if (targets.Count == 0) return;

                double c = 343.0;
                double[] seed_ms = new double[Elements.Count];

                for (int t = 0; t < targets.Count; t++)
                {
                    double[] r = new double[Elements.Count];
                    double r_ref = double.NegativeInfinity;

                    for (int i = 0; i < Elements.Count; i++)
                    {
                        Rhino.Geometry.Point3d src = Elements[i].Geometry.GetBoundingBox(true).Min;
                        r[i] = src.DistanceTo(targets[t]);
                        if (r[i] > r_ref) r_ref = r[i];
                    }

                    for (int i = 0; i < Elements.Count; i++)
                    {
                        seed_ms[i] += (r_ref - r[i]) / c * 1000.0;
                    }
                }

                for (int i = 0; i < seed_ms.Length; i++)
                {
                    seed_ms[i] /= targets.Count;
                }

                NormalizeDelays(seed_ms);

                double[][] delay_by_element = new double[Elements.Count][];
                double[][] phase_by_element = new double[Elements.Count][];

                for (int i = 0; i < Elements.Count; i++)
                {
                    delay_by_element[i] = new double[8];
                    phase_by_element[i] = new double[8];
                }

                for (int oct = 0; oct < 8; oct++)
                {
                    double[] delay_ms = new double[seed_ms.Length];
                    Array.Copy(seed_ms, delay_ms, seed_ms.Length);
                    double f = 62.5 * Math.Pow(2, oct);

                    if (fineTune)
                    {
                        double[] best = new double[delay_ms.Length];
                        Array.Copy(delay_ms, best, delay_ms.Length);

                        // Start with roughly 45 degrees of phase at this octave.
                        double step_ms = 45.0 / 360.0 / f * 1000.0;

                        double bestScore = ArrayPatternScore(targets, best, oct);

                        for (int pass = 0; pass < 8; pass++)
                        {
                            bool improved = false;

                            for (int i = 0; i < best.Length; i++)
                            {
                                double original = best[i];

                                best[i] = original + step_ms;
                                NormalizeDelays(best);
                                double plusScore = ArrayPatternScore(targets, best, oct);

                                if (plusScore > bestScore)
                                {
                                    bestScore = plusScore;
                                    improved = true;
                                    continue;
                                }

                                best[i] = original - step_ms;
                                NormalizeDelays(best);
                                double minusScore = ArrayPatternScore(targets, best, oct);

                                if (minusScore > bestScore)
                                {
                                    bestScore = minusScore;
                                    improved = true;
                                    continue;
                                }

                                best[i] = original;
                                NormalizeDelays(best);
                            }

                            if (!improved)
                            {
                                step_ms *= 0.5;
                            }
                        }

                        NormalizeDelays(best);
                    }

                    NormalizeDelays(delay_ms);
                    for (int i = 0; i < Elements.Count; i++)
                    {
                        delay_by_element[i][oct] = delay_ms[i];
                        phase_by_element[i][oct] = delay_ms[i] / 1000.0 * f * 360.0;
                    }
                }

                for (int i = 0; i < Elements.Count; i++)
                {
                    RhinoObject obj = Elements[i];

                    if (obj == null || obj.Geometry == null) continue;

                    obj.Geometry.SetUserString(
                        "ArrayPhaseOctaveDeg",
                        PachTools.EncodeEight(phase_by_element[i]));

                    obj.Geometry.SetUserString(
                        "ArrayDelayOctaveMs",
                        PachTools.EncodeEight(delay_by_element[i]));

                    EnsureSourceInConduit(obj);
                }

                RebuildElementEditors();
                UpdatePatternConduit();

                Rhino.RhinoDoc.ActiveDoc.Views.Redraw();
            }

            private static void NormalizeDelays(double[] delay)
            {
                if (delay == null || delay.Length == 0) return;

                double min = double.PositiveInfinity;

                for (int i = 0; i < delay.Length; i++)
                {
                    if (delay[i] < min) min = delay[i];
                }

                if (double.IsInfinity(min)) return;

                for (int i = 0; i < delay.Length; i++)
                {
                    delay[i] -= min;
                }
            }

            private double ArrayPatternScore(List<Rhino.Geometry.Point3d> targets, double[] delay_ms, int octave)
            {
                double targetPower = 0;

                for (int i = 0; i < targets.Count; i++)
                {
                    double mag = ArrayMagnitudeAtPoint(targets[i], delay_ms, octave);
                    targetPower += mag * mag;
                }

                targetPower /= Math.Max(1, targets.Count);

                List<double> sidePowers = new List<double>();

                Point3d Center;

                if (Elements == null || Elements.Count == 0) Center = Rhino.Geometry.Point3d.Origin;
                else
                {
                    double x = 0;
                    double y = 0;
                    double z = 0;
                    int count = 0;

                    for (int i = 0; i < Elements.Count; i++)
                    {
                        RhinoObject obj = Elements[i];

                        if (obj == null || obj.Geometry == null) continue;

                        Rhino.Geometry.Point3d pt = obj.Geometry.GetBoundingBox(true).Min;

                        x += pt.X;
                        y += pt.Y;
                        z += pt.Z;
                        count++;
                    }
                    if (count == 0) Center = Rhino.Geometry.Point3d.Origin;
                    else Center = new Rhino.Geometry.Point3d(x / count, y / count, z / count);
                }

                Hare.Geometry.Topology sphere = Utilities.Geometry.GeoSphere(2).Model[0];
                Rhino.Geometry.Mesh mesh = Utilities.RCPachTools.HaretoRhinoMesh(sphere, true);

                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    Rhino.Geometry.Vector3d dir = new Rhino.Geometry.Vector3d(mesh.Vertices[i].X, mesh.Vertices[i].Y, mesh.Vertices[i].Z);

                    if (!dir.Unitize()) continue;
                    double cosLimit = Math.Cos(12.0 * Math.PI / 180.0);
                    bool limit_exceeded = false;

                    for (int j = 0; j < targets.Count; j++)
                    {
                        Rhino.Geometry.Vector3d tdir = targets[j] - Center;
                        if (!tdir.Unitize()) continue;
                        if (dir * tdir >= cosLimit) limit_exceeded = true;
                    }

                    if (limit_exceeded) continue;
                    Rhino.Geometry.Point3d sample = Center + dir * PatternReferenceDistance.Value;
                    double mag = ArrayMagnitudeAtPoint(sample, delay_ms, octave);
                    sidePowers.Add(mag * mag);
                }

                if (sidePowers.Count == 0)
                {
                    return 10.0 * Math.Log10(Math.Max(1E-12, targetPower));
                }

                sidePowers.Sort();
                sidePowers.Reverse();

                double maxSide = sidePowers[0];

                int topCount = Math.Max(1, sidePowers.Count / 10);
                double topSide = 0;

                for (int i = 0; i < topCount; i++)
                {
                    topSide += sidePowers[i];
                }

                topSide /= topCount;

                double targetDb = 10.0 * Math.Log10(Math.Max(1E-12, targetPower));
                double maxSideDb = 10.0 * Math.Log10(Math.Max(1E-12, maxSide));
                double topSideDb = 10.0 * Math.Log10(Math.Max(1E-12, topSide));

                double smooth = 0;

                for (int i = 1; i < delay_ms.Length - 1; i++)
                {
                    double d2 = delay_ms[i - 1] - 2.0 * delay_ms[i] + delay_ms[i + 1];
                    smooth += d2 * d2;
                }

                return targetDb - 0.75 * maxSideDb - 0.15 * topSideDb - 0.02 * smooth;
            }

            private double ArrayMagnitudeAtPoint(Rhino.Geometry.Point3d target, double[] delay_ms, int octave)
            {
                double omega = Utilities.Numerics.angularFrequency_Octave[octave];
                double k = omega / 343.0;

                System.Numerics.Complex sum = System.Numerics.Complex.Zero;

                for (int i = 0; i < Elements.Count; i++)
                {
                    Rhino.Geometry.Point3d src =
                        Elements[i].Geometry.GetBoundingBox(true).Min;

                    double r = src.DistanceTo(target);
                    double tau = delay_ms[i] / 1000.0;

                    double phase = -k * r - omega * tau;

                    sum += System.Numerics.Complex.FromPolarCoordinates(1.0, phase);
                }

                return sum.Magnitude;
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
                ElementLayout.Padding = 2;
                ElementLayout.DefaultSpacing = new Eto.Drawing.Size(4, 4);

                List<Control> row = new List<Control>();

                for (int i = 0; i < Elements.Count; i++)
                {
                    row.Add(new ArrayElementEditor(Elements[i], OnElementChanged));
                }

                ElementLayout.AddRow(row.ToArray());

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
                layout.Padding = 4;
                layout.DefaultSpacing = new Eto.Drawing.Size(2, 2);

                Alt = NewAngleStepper(-90, 90);
                Azi = NewAngleStepper(-360, 360);
                Axial = NewAngleStepper(-360, 360);

                Alt.ValueChanged += ValueChanged;
                Azi.ValueChanged += ValueChanged;
                Axial.ValueChanged += ValueChanged;

                for (int i = 0; i < 8; i++)
                {
                    OctPhaseDelay[i] = new NumericStepper();
                    OctPhaseDelay[i].DecimalPlaces = 0;
                    OctPhaseDelay[i].MinValue = -36000;
                    OctPhaseDelay[i].MaxValue = 36000;
                    OctPhaseDelay[i].Increment = 5;
                    OctPhaseDelay[i].Width = 58;
                    OctPhaseDelay[i].ValueChanged += ValueChanged;
                }

                layout.AddRow(new Label { Text = "Alt", Width = 28 }, Alt);
                layout.AddRow(new Label { Text = "Azi", Width = 28 }, Azi);
                layout.AddRow(new Label { Text = "Ax", Width = 28 }, Axial);

                layout.AddRow(new Label { Text = "Phase °" });

                layout.AddRow(new Label { Text = "63", Width = 28 }, OctPhaseDelay[0]);
                layout.AddRow(new Label { Text = "125", Width = 28 }, OctPhaseDelay[1]);
                layout.AddRow(new Label { Text = "250", Width = 28 }, OctPhaseDelay[2]);
                layout.AddRow(new Label { Text = "500", Width = 28 }, OctPhaseDelay[3]);
                layout.AddRow(new Label { Text = "1k", Width = 28 }, OctPhaseDelay[4]);
                layout.AddRow(new Label { Text = "2k", Width = 28 }, OctPhaseDelay[5]);
                layout.AddRow(new Label { Text = "4k", Width = 28 }, OctPhaseDelay[6]);
                layout.AddRow(new Label { Text = "8k", Width = 28 }, OctPhaseDelay[7]);

                Button zeroDelays = new Button();
                zeroDelays.Text = "Zero";
                zeroDelays.Width = 58;
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

                double[] phase = PachTools.DecodeEight(Obj.Geometry.GetUserString("ArrayPhaseOctaveDeg"));

                for (int i = 0; i < 8; i++)
                {
                    OctPhaseDelay[i].Value = phase[i];
                }

                Loading = false;
            }

            private NumericStepper NewAngleStepper(double min, double max)
            {
                NumericStepper s = new NumericStepper();
                s.DecimalPlaces = 1;
                s.MinValue = min;
                s.MaxValue = max;
                s.Increment = 1;
                s.Width = 58;
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