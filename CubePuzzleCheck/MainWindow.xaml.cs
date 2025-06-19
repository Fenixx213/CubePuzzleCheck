using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;

namespace CubePuzzleCheck
{
    public partial class MainWindow : Window
    {
        private Point _lastMousePosition;
        private bool _isRotating = false;
        private Random _random = new Random();
        private List<Point3D> _targetCubes = new List<Point3D>();
        private List<Point3D> _userCubes = new List<Point3D>();
        private int _maxCubes = 4; // Maximum number of cubes in the puzzle
        private GeometryModel3D _previewCube;
        private GeometryModel3D _previewWireframe;
        private int _previewX, _previewY, _previewZ;
        private List<GeometryModel3D> _debugMarkers = new List<GeometryModel3D>();
        private Dictionary<Point3D, (GeometryModel3D cube, GeometryModel3D wireframe)> _cubeModels = new Dictionary<Point3D, (GeometryModel3D, GeometryModel3D)>(); // Track cube models
        private double _theta = Math.PI; // Azimuth (horizontal angle)
        private double _phi = Math.PI / 2;   // Elevation (vertical angle)
        private readonly double _radius = 10; // Distance from camera to origin
        private readonly double _rotationSpeed = 0.005; // Adjusted for smoother camera rotation

        public MainWindow()
        {
            InitializeComponent();
            InitializePreviewCube();
            SetupCamera();
            GenerateNewPuzzle(null, null); // Generate initial puzzle
            MainViewport.MouseMove += MainViewport_MouseMove;
            MainViewport.SizeChanged += (s, e) => UpdateViewportSize(); // Handle viewport resizing
        }

        private void SetupCamera()
        {
            var camera = MainViewport.Camera as PerspectiveCamera;
            if (camera != null)
            {
                camera.Position = new Point3D(0, 0, _radius);
                camera.LookDirection = new Vector3D(0, 0, -_radius);
                camera.UpDirection = new Vector3D(0, 1, 0);
                camera.FieldOfView = 60;
                UpdateCameraPosition();
            }
        }

        private void UpdateCameraPosition()
        {
            var camera = MainViewport.Camera as PerspectiveCamera;
            if (camera == null) return;

            // Clamp phi to avoid flipping at the poles
            _phi = Math.Max(0.1, Math.Min(Math.PI - 0.1, _phi));

            // Convert spherical coordinates to Cartesian coordinates
            double x = -_radius * Math.Sin(_phi) * Math.Cos(_theta);
            double y = _radius * Math.Cos(_phi);
            double z = _radius * Math.Sin(_phi) * Math.Sin(_theta);

            camera.Position = new Point3D(x + 2, y + 3, z);
            camera.LookDirection = new Vector3D(-x, -y - 3, -z + 2);
        }

        private void InitializePreviewCube()
        {
            _previewCube = CreateCube(0, 0, 0, Brushes.YellowGreen);
            _previewWireframe = CreateCubeWireframe();
            MainModelGroup.Children.Add(_previewCube);
            MainModelGroup.Children.Add(_previewWireframe);
        }

        private void UpdateViewportSize()
        {
            MainViewport.ClipToBounds = true;
        }

        private void GenerateNewPuzzle(object sender, RoutedEventArgs e)
        {
            _targetCubes.Clear();
            _userCubes.Clear();
            _cubeModels.Clear(); // Clear cube models
            MainModelGroup.Children.Clear();
            TopViewCanvas.Children.Clear();
            FrontViewCanvas.Children.Clear();
            LeftViewCanvas.Children.Clear();

            // Create solid gray platform
            var platformGeometry = new MeshGeometry3D();
            for (int x = 0; x < 4; x++)
            {
                for (int z = 0; z < 4; z++)
                {
                    platformGeometry.Positions.Add(new Point3D(x, 0, z));
                    platformGeometry.Positions.Add(new Point3D(x + 1, 0, z));
                    platformGeometry.Positions.Add(new Point3D(x + 1, 0, z + 1));
                    platformGeometry.Positions.Add(new Point3D(x, 0, z + 1));

                    int baseIndex = (x * 4 + z) * 4;
                    platformGeometry.TriangleIndices.Add(baseIndex);
                    platformGeometry.TriangleIndices.Add(baseIndex + 1);
                    platformGeometry.TriangleIndices.Add(baseIndex + 2);
                    platformGeometry.TriangleIndices.Add(baseIndex);
                    platformGeometry.TriangleIndices.Add(baseIndex + 2);
                    platformGeometry.TriangleIndices.Add(baseIndex + 3);
                }
            }
            var platformMaterial = new DiffuseMaterial(Brushes.Gray);
            var platformModel = new GeometryModel3D(platformGeometry, platformMaterial);
            platformModel.BackMaterial = new DiffuseMaterial(Brushes.Gray); // Ensure both sides are gray
            MainModelGroup.Children.Add(platformModel);

            // Add border lines
            var linesGeometry = new MeshGeometry3D();
            for (int x = 0; x <= 4; x++)
            {
                linesGeometry.Positions.Add(new Point3D(x, 0, 0));
                linesGeometry.Positions.Add(new Point3D(x, 0, 4));
            }
            for (int z = 0; z <= 4; z++)
            {
                linesGeometry.Positions.Add(new Point3D(0, 0, z));
                linesGeometry.Positions.Add(new Point3D(4, 0, z));
            }
            var linesModel = new GeometryModel3D(linesGeometry, new DiffuseMaterial(Brushes.Gray));
            MainModelGroup.Children.Add(linesModel);
            MainModelGroup.Children.Add(new AmbientLight(Color.FromRgb(255, 255, 255)));

            // Generate random cubes with vertical or horizontal connections, up to _maxCubes
            int numCubes = _random.Next(2, _maxCubes + 1); // From 2 to _maxCubes (5)
            int startX = _random.Next(0, 4);
            int startZ = _random.Next(0, 4);
            _targetCubes.Add(new Point3D(startX, 0, startZ));

            for (int i = 1; i < numCubes; i++)
            {
                List<(int x, int y, int z)> possiblePositions = new List<(int, int, int)>();
                foreach (var cube in _targetCubes)
                {
                    int x = (int)cube.X;
                    int y = (int)cube.Y;
                    int z = (int)cube.Z;

                    // Vertical placement (above current cube)
                    if (y < 3)
                    {
                        int newY = y + 1;
                        if (!_targetCubes.Any(p => p.X == x && p.Y == newY && p.Z == z) && IsValidPlacement(x, newY, z))
                        {
                            possiblePositions.Add((x, newY, z));
                        }
                    }

                    // Horizontal placement (adjacent in XZ plane, same Y)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (dx == 0 && dz == 0) continue;
                            if (dx != 0 && dz != 0) continue; // Only allow straight neighbors (not diagonal)
                            int newX = x + dx;
                            int newZ = z + dz;
                            if (newX >= 0 && newX < 4 && newZ >= 0 && newZ < 4 &&
                                !_targetCubes.Any(p => p.X == newX && p.Y == y && p.Z == newZ) && IsValidPlacement(newX, y, newZ))
                            {
                                possiblePositions.Add((newX, y, newZ));
                            }
                        }
                    }
                }
                if (possiblePositions.Count > 0)
                {
                    var pos = possiblePositions[_random.Next(possiblePositions.Count)];
                    _targetCubes.Add(new Point3D(pos.x, pos.y, pos.z));
                }
            }
            // Create user cubes by copying target cubes
            _userCubes.AddRange(_targetCubes);

            // Remove two random correct cubes from user cubes (if possible)
            if (_userCubes.Count >= 2)
            {
                for (int i = 0; i < 2; i++)
                {
                    if (_userCubes.Count > 0)
                    {
                        int removeIndex = _random.Next(_userCubes.Count);
                        _userCubes.RemoveAt(removeIndex);
                    }
                }
            }

            // Add two incorrect cubes to user cubes, ensuring they are adjacent to correct cubes
            for (int i = 0; i < 2; i++)
            {
                List<(int x, int y, int z)> possibleIncorrectPositions = new List<(int, int, int)>();
                // Iterate over current user cubes (which are initially copies of target cubes)
                foreach (var cube in _userCubes)
                {
                    int x = (int)cube.X;
                    int y = (int)cube.Y;
                    int z = (int)cube.Z;

                    // Check vertical placement (above current cube)
                    if (y < 3)
                    {
                        int newY = y + 1;
                        if (!_targetCubes.Any(p => (int)p.X == x && (int)p.Y == newY && (int)p.Z == z) &&
                            !_userCubes.Any(p => (int)p.X == x && (int)p.Y == newY && (int)p.Z == z) &&
                            IsValidPlacement(x, newY, z))
                        {
                            possibleIncorrectPositions.Add((x, newY, z));
                        }
                    }

                    // Check horizontal placement (adjacent in XZ plane, same Y)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (dx == 0 && dz == 0) continue;
                            if (dx != 0 && dz != 0) continue; // Only allow straight neighbors (not diagonal)
                            int newX = x + dx;
                            int newZ = z + dz;
                            if (newX >= 0 && newX < 4 && newZ >= 0 && newZ < 4 &&
                                !_targetCubes.Any(p => (int)p.X == newX && (int)p.Y == y && (int)p.Z == newZ) &&
                                !_userCubes.Any(p => (int)p.X == newX && (int)p.Y == y && (int)p.Z == newZ) &&
                                IsValidPlacement(newX, y, newZ))
                            {
                                possibleIncorrectPositions.Add((newX, y, newZ));
                            }
                        }
                    }
                }

                if (possibleIncorrectPositions.Count > 0)
                {
                    var pos = possibleIncorrectPositions[_random.Next(possibleIncorrectPositions.Count)];
                    _userCubes.Add(new Point3D(pos.x, pos.y, pos.z));
                }
            }

            // Create and add user cubes to the 3D scene
            foreach (var cubePos in _userCubes)
            {
                int x = (int)cubePos.X;
                int y = (int)cubePos.Y;
                int z = (int)cubePos.Z;
                var cube = CreateCube(x, y, z); // Use random color for user cubes
                var wireframe = CreateCubeWireframe();
                wireframe.Transform = new TranslateTransform3D(x + 0.5, y + 0.5, z + 0.5);
                _cubeModels[cubePos] = (cube, wireframe);
                MainModelGroup.Children.Add(cube);
                MainModelGroup.Children.Add(wireframe);
            }
            // Update preview cube position
            if (_userCubes.Any())
            {
                var lastCube = _userCubes.Last();
                UpdatePreviewCubePosition((int)lastCube.X, (int)lastCube.Y + 1, (int)lastCube.Z);
            }
            else
            {
                UpdatePreviewCubePosition(0, 0, 0);
            }

            DrawViews();
            MainModelGroup.Children.Add(_previewCube);
            MainModelGroup.Children.Add(_previewWireframe);
        }

        private void DrawViews()
        {
            TopViewCanvas.Children.Clear();
            FrontViewCanvas.Children.Clear();
            LeftViewCanvas.Children.Clear();

            // Calculate 2D projections
            var views = Calculate2DViews();

            // Draw Top View (X vs Z)
            var topView = views["Top"];
            for (int x = 0; x < topView.GetLength(0); x++)
            {
                for (int z = 0; z < topView.GetLength(1); z++)
                {
                    if (topView[x, z])
                    {
                        var rect = new Rectangle
                        {
                            Width = 25,
                            Height = 25,
                            Fill = Brushes.Yellow
                        };
                        Canvas.SetTop(rect, x * 25);
                        Canvas.SetLeft(rect, (topView.GetLength(1) - 1 - z) * 25);
                        TopViewCanvas.Children.Add(rect);
                    }
                }
            }

            // Draw Front View (X vs Y, inverted Y)
            var leftView = views["Left"];
            for (int x = 0; x < leftView.GetLength(0); x++)
            {
                for (int y = 0; y < leftView.GetLength(1); y++)
                {
                    if (leftView[x, y])
                    {
                        var rect = new Rectangle
                        {
                            Width = 25,
                            Height = 25,
                            Fill = Brushes.Yellow
                        };
                        Canvas.SetLeft(rect, (leftView.GetLength(0) - 1 - x) * 25);
                        Canvas.SetTop(rect, (leftView.GetLength(1) - 1 - y) * 25);
                        LeftViewCanvas.Children.Add(rect);
                    }
                }
            }

            // Draw Left View (Z vs Y, rotated and inverted Z)
            var frontView = views["Front"];
            for (int z = 0; z < frontView.GetLength(0); z++)
            {
                for (int y = 0; y < frontView.GetLength(1); y++)
                {
                    if (frontView[z, y])
                    {
                        var rect = new Rectangle
                        {
                            Width = 25,
                            Height = 25,
                            Fill = Brushes.Yellow
                        };
                        Canvas.SetTop(rect, (frontView.GetLength(1) - 1 - y) * 25);
                        Canvas.SetLeft(rect, (z) * 25);
                        FrontViewCanvas.Children.Add(rect);
                    }
                }
            }
        }

        private Dictionary<string, bool[,]> Calculate2DViews()
        {
            if (_targetCubes == null || _targetCubes.Count == 0)
                return new Dictionary<string, bool[,]>();

            // Find min and max coordinates to determine array bounds
            int minX = (int)_targetCubes.Min(p => p.X);
            int maxX = (int)_targetCubes.Max(p => p.X);
            int minY = (int)_targetCubes.Min(p => p.Y);
            int maxY = (int)_targetCubes.Max(p => p.Y);
            int minZ = (int)_targetCubes.Min(p => p.Z);
            int maxZ = (int)_targetCubes.Max(p => p.Z);

            // Calculate array dimensions with offsets, capped at 4x4x4 grid
            int widthX = Math.Min(maxX - minX + 1, 4);
            int heightY = Math.Min(maxY - minY + 1, 4);
            int depthZ = Math.Min(maxZ - minZ + 1, 4);

            // Initialize 2D arrays with proper sizes
            bool[,] topView = new bool[widthX, depthZ];    // X vs Z (top view)
            bool[,] frontView = new bool[depthZ, heightY];  // Z vs Y (left view)
            bool[,] leftView = new bool[widthX, heightY];   // X vs Y (front view)

            // Populate arrays only with supported cube positions
            foreach (var cube in _targetCubes)
            {
                int x = (int)cube.X - minX;
                int y = (int)cube.Y - minY;
                int z = (int)cube.Z - minZ;

                // Check if the cube is supported (y=0 or has a cube below at y-1)
                bool isSupported = (y == 0) || _targetCubes.Any(p => (int)p.X == x + minX && (int)p.Y == y - 1 + minY && (int)p.Z == z + minZ);

                if (x >= 0 && x < widthX && y >= 0 && y < heightY && z >= 0 && z < depthZ && isSupported)
                {
                    topView[x, z] = true;
                    leftView[x, y] = true;
                    frontView[z, y] = true;
                }
            }

            return new Dictionary<string, bool[,]>
            {
                { "Top", topView },
                { "Left", frontView },
                { "Front", leftView }
            };
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.RightButton == MouseButtonState.Pressed && !Keyboard.IsKeyDown(Key.LeftCtrl))
            {
                _lastMousePosition = e.GetPosition(this);
                _isRotating = true;
                CaptureMouse();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isRotating)
            {
                Point currentPosition = e.GetPosition(this);
                double deltaX = currentPosition.X - _lastMousePosition.X;
                double deltaY = currentPosition.Y - _lastMousePosition.Y;

                // Update angles for horizontal (theta) and vertical (phi) rotation
                _theta -= deltaX * _rotationSpeed;
                _phi -= deltaY * _rotationSpeed;

                UpdateCameraPosition();
                _lastMousePosition = currentPosition;
            }
        }

        private void MainViewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (_previewCube == null) return;

            var mousePos = e.GetPosition(MainViewport);
            var ray = GetRayFromMouse(mousePos);
            if (ray == null) return;

            // Try to intersect with top faces of existing cubes
            var intersectionResult = GetCubeTopIntersection(ray);
            if (intersectionResult.HasValue)
            {
                var (x, y, z) = intersectionResult.Value;
                if (x >= 0 && x < 4 && z >= 0 && z < 4)
                {
                    UpdatePreviewCubePosition(x, y, z);
                    _previewX = x;
                    _previewY = y;
                    _previewZ = z;
                    AddDebugMarker(new Point3D(x + 0.5, y + 0.5, z + 0.5));
                }
            }
            else
            {
                // Fallback to ground plane intersection
                if (Math.Abs(ray.Direction.Y) > 1e-6)
                {
                    double t = -ray.Origin.Y / ray.Direction.Y;
                    if (t >= 0)
                    {
                        var intersection = ray.Origin + t * ray.Direction;
                        var (x, y, z) = SnapToGrid(intersection);
                        if (x >= 0 && x < 4 && z >= 0 && z < 4)
                        {
                            UpdatePreviewCubePosition(x, y, z);
                            _previewX = x;
                            _previewY = y;
                            _previewZ = z;
                            AddDebugMarker(intersection);
                        }
                    }
                }
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (_isRotating)
            {
                _isRotating = false;
                ReleaseMouseCapture();
            }
        }

        private void MainViewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var mousePos = e.GetPosition(MainViewport);
            var ray = GetRayFromMouse(mousePos);
            if (ray == null) return;

            if (e.LeftButton == MouseButtonState.Pressed && !Keyboard.IsKeyDown(Key.LeftCtrl))
            {
                // Try to intersect with top faces of existing cubes or ground
                var intersectionResult = GetCubeTopIntersection(ray);
                if (intersectionResult.HasValue)
                {
                    var (x, y, z) = intersectionResult.Value;
                    if (x >= 0 && x < 4 && z >= 0 && z < 4 &&
                        !_userCubes.Any(p => (int)p.X == x && (int)p.Y == y && (int)p.Z == z) &&
                        IsValidPlacement(x, y, z))
                    {
                        _userCubes.Add(new Point3D(x, y, z));
                        var cube = CreateCube(x, y, z);
                        var wireframe = CreateCubeWireframe();
                        wireframe.Transform = new TranslateTransform3D(x + 0.5, y + 0.5, z + 0.5);
                        _cubeModels[new Point3D(x, y, z)] = (cube, wireframe);
                        MainModelGroup.Children.Add(cube);
                        MainModelGroup.Children.Add(wireframe);
                        UpdatePreviewCubePosition(x, y + 1, z); // Move preview to next stack position
                    }
                }
            }
            else if (e.RightButton == MouseButtonState.Pressed && !Keyboard.IsKeyDown(Key.LeftCtrl))
            {
                // Check for cube intersection to remove
                var cubeToRemove = GetCubeIntersection(ray);
                if (cubeToRemove.HasValue)
                {
                    RemoveCube(cubeToRemove.Value);
                }
            }
        }

        private void RemoveCube(Point3D cubePos)
        {
            if (_cubeModels.TryGetValue(cubePos, out var models))
            {
                // Remove from scene and models
                MainModelGroup.Children.Remove(models.cube);
                MainModelGroup.Children.Remove(models.wireframe);
                _cubeModels.Remove(cubePos);
                _userCubes.Remove(cubePos);

                // Update preview cube position
                int x = (int)cubePos.X;
                int y = (int)cubePos.Y - 1; // Move preview to the position below the removed cube
                int z = (int)cubePos.Z;
                if (y >= 0 && !_userCubes.Any(p => (int)p.X == x && (int)p.Y == y && (int)p.Z == z))
                {
                    UpdatePreviewCubePosition(x, y, z);
                }
                else
                {
                    // If no valid position below, move to ground or adjacent valid position
                    var validPos = _userCubes.FirstOrDefault(p => IsValidPlacement((int)p.X, (int)p.Y + 1, (int)p.Z));
                    if (validPos != null)
                    {
                        UpdatePreviewCubePosition((int)validPos.X, (int)validPos.Y + 1, (int)validPos.Z);
                    }
                    else
                    {
                        UpdatePreviewCubePosition(0, 0, 0); // Default to origin if no cubes left
                    }
                }
            }
        }

        private Point3D? GetCubeIntersection(Ray3D ray)
        {
            double closestT = double.MaxValue;
            Point3D? result = null;

            foreach (var cube in _userCubes)
            {
                int x = (int)cube.X;
                int y = (int)cube.Y;
                int z = (int)cube.Z;

                // Check all six faces of the cube
                var faces = new[]
                {
                    (new Point3D(x + 0.5, y, z + 0.5), new Vector3D(0, -1, 0)), // Bottom
                    (new Point3D(x + 0.5, y + 1, z + 0.5), new Vector3D(0, 1, 0)), // Top
                    (new Point3D(x, y + 0.5, z + 0.5), new Vector3D(-1, 0, 0)), // Left
                    (new Point3D(x + 1, y + 0.5, z + 0.5), new Vector3D(1, 0, 0)), // Right
                    (new Point3D(x + 0.5, y + 0.5, z), new Vector3D(0, 0, -1)), // Front
                    (new Point3D(x + 0.5, y + 0.5, z + 1), new Vector3D(0, 0, 1))  // Back
                };

                foreach (var (planePoint, normal) in faces)
                {
                    double denominator = Vector3D.DotProduct(normal, ray.Direction);
                    if (Math.Abs(denominator) > 1e-6)
                    {
                        Vector3D planeToRay = planePoint - ray.Origin;
                        double t = Vector3D.DotProduct(planeToRay, normal) / denominator;
                        if (t >= 0 && t < closestT)
                        {
                            var intersection = ray.Origin + t * ray.Direction;
                            // Check if intersection is within cube face bounds
                            if (intersection.X >= x - 0.05 && intersection.X <= x + 1.05 &&
                                intersection.Y >= y - 0.05 && intersection.Y <= y + 1.05 &&
                                intersection.Z >= z - 0.05 && intersection.Z <= z + 1.05)
                            {
                                closestT = t;
                                result = cube;
                            }
                        }
                    }
                }
            }

            return result;
        }

        private bool IsValidPlacement(int x, int y, int z)
        {
            // Ensure the position is within the 4x4x4 grid boundaries
            if (x < 0 || x >= 4 || y < 0 || y >= 4 || z < 0 || z >= 4)
                return false;

            // Check if the position is already occupied
            if (_userCubes.Any(p => (int)p.X == x && (int)p.Y == y && (int)p.Z == z))
                return false;

            // Allow placement only on the ground (y=0) or directly on top of an existing cube
            if (y == 0)
            {
                return true; // Ground placement is always valid
            }
            else
            {
                // Require a cube directly beneath at (x, y-1, z)
                return _userCubes.Any(p => (int)p.X == x && (int)p.Y == y - 1 && (int)p.Z == z) ||
                       _targetCubes.Any(p => (int)p.X == x && (int)p.Y == y - 1 && (int)p.Z == z);
            }
        }

        private (int x, int y, int z)? GetCubeTopIntersection(Ray3D ray)
        {
            double closestT = double.MaxValue;
            (int x, int y, int z)? result = null;

            // Check intersection with each cube's top face (using only _userCubes)
            foreach (var cube in _userCubes)
            {
                int x = (int)cube.X;
                int y = (int)cube.Y;
                int z = (int)cube.Z;

                // Check top face of the cube (y = cube.Y + 1)
                double topY = y + 1;
                if (Math.Abs(ray.Direction.Y) > 1e-6)
                {
                    double t = (topY - ray.Origin.Y) / ray.Direction.Y;
                    if (t >= 0 && t < closestT)
                    {
                        var intersection = ray.Origin + t * ray.Direction;
                        // Check if intersection is within cube's top face bounds with tolerance
                        if (intersection.X >= x - 0.05 && intersection.X <= x + 1.05 &&
                            intersection.Z >= z - 0.05 && intersection.Z <= z + 1.05 &&
                            IsValidPlacement(x, y + 1, z))
                        {
                            closestT = t;
                            result = (x, y + 1, z); // Place new cube on top
                        }
                    }
                }
            }

            // If no cube top is hit, try ground plane at y=0
            if (!result.HasValue && Math.Abs(ray.Direction.Y) > 1e-6)
            {
                double t = -ray.Origin.Y / ray.Direction.Y;
                if (t >= 0 && t < closestT)
                {
                    var intersection = ray.Origin + t * ray.Direction;
                    int x = Clamp((int)Math.Round(intersection.X), 0, 3);
                    int z = Clamp((int)Math.Round(intersection.Z), 0, 3);
                    int y = 0; // Ground level
                    if (IsValidPlacement(x, y, z))
                    {
                        result = (x, y, z);
                    }
                }
            }

            return result;
        }

        private GeometryModel3D CreateCube(int x, int y, int z, Brush color = null)
        {
            var mesh = new MeshGeometry3D();
            double halfSize = 0.5;
            double centerX = x + halfSize;
            double centerY = y + halfSize;
            double centerZ = z + halfSize;

            var points = new Point3DCollection
            {
                new Point3D(centerX - halfSize, centerY - halfSize, centerZ - halfSize),
                new Point3D(centerX + halfSize, centerY - halfSize, centerZ - halfSize),
                new Point3D(centerX + halfSize, centerY + halfSize, centerZ - halfSize),
                new Point3D(centerX - halfSize, centerY + halfSize, centerZ - halfSize),
                new Point3D(centerX - halfSize, centerY - halfSize, centerZ + halfSize),
                new Point3D(centerX + halfSize, centerY - halfSize, centerZ + halfSize),
                new Point3D(centerX + halfSize, centerY + halfSize, centerZ + halfSize),
                new Point3D(centerX - halfSize, centerY + halfSize, centerZ + halfSize)
            };

            var indices = new Int32Collection
            {
                0,1,2, 0,2,3,
                4,6,5, 4,7,6,
                0,3,7, 0,7,4,
                1,5,6, 1,6,2,
                3,2,6, 3,6,7,
                0,4,5, 0,5,1
            };

            mesh.Positions = points;
            mesh.TriangleIndices = indices;

            Brush cubeBrush = color ?? new SolidColorBrush(Color.FromRgb((byte)_random.Next(256), (byte)_random.Next(256), (byte)_random.Next(256)));
            var cubeModel = new GeometryModel3D
            {
                Geometry = mesh,
                Material = new DiffuseMaterial(cubeBrush),
                BackMaterial = new DiffuseMaterial(cubeBrush)
            };

            return cubeModel;
        }

        private GeometryModel3D CreateCubeWireframe()
        {
            MeshGeometry3D wireframeMesh = new MeshGeometry3D();
            double size = 0.5;
            double thickness = 0.015;

            Point3D[] vertices = new Point3D[]
            {
                new Point3D(-size, -size, -size),
                new Point3D(size, -size, -size),
                new Point3D(size, size, -size),
                new Point3D(-size, size, -size),
                new Point3D(-size, -size, size),
                new Point3D(size, -size, size),
                new Point3D(size, size, size),
                new Point3D(-size, size, size)
            };

            int[][] edges = new int[][]
            {
                new[] { 0, 1 }, new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 0 },
                new[] { 4, 5 }, new[] { 5, 6 }, new[] { 6, 7 }, new[] { 7, 4 },
                new[] { 0, 4 }, new[] { 1, 5 }, new[] { 2, 6 }, new[] { 3, 7 }
            };

            Point3DCollection positions = new Point3DCollection();
            Int32Collection indices = new Int32Collection();

            for (int i = 0; i < edges.Length; i++)
            {
                Point3D start = vertices[edges[i][0]];
                Point3D end = vertices[edges[i][1]];
                Vector3D dir = end - start;
                dir.Normalize();

                Vector3D up = Math.Abs(dir.Y) < 0.9 ? new Vector3D(0, 1, 0) : new Vector3D(0, 0, 1);
                Vector3D right = Vector3D.CrossProduct(up, dir);
                right.Normalize();
                up = Vector3D.CrossProduct(dir, right);
                up.Normalize();

                Point3D[] crossSectionStart = new Point3D[4];
                Point3D[] crossSectionEnd = new Point3D[4];
                for (int j = 0; j < 4; j++)
                {
                    double angle = j * Math.PI / 2;
                    Vector3D offset = (Math.Cos(angle) * right + Math.Sin(angle) * up) * thickness;
                    crossSectionStart[j] = start + offset;
                    crossSectionEnd[j] = end + offset;
                }

                int baseIndex = positions.Count;
                foreach (var p in crossSectionStart) positions.Add(p);
                foreach (var p in crossSectionEnd) positions.Add(p);

                int[] sideIndices = new int[]
                {
                    0, 1, 5, 0, 5, 4,
                    1, 2, 6, 1, 6, 5,
                    2, 3, 7, 2, 7, 6,
                    3, 0, 4, 3, 4, 7
                };
                foreach (int idx in sideIndices)
                {
                    indices.Add(baseIndex + idx);
                }
            }

            wireframeMesh.Positions = positions;
            wireframeMesh.TriangleIndices = indices;

            return new GeometryModel3D
            {
                Geometry = wireframeMesh,
                Material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0, 0, 0))),
                BackMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0, 0, 0)))
            };
        }

        private void UpdatePreviewCubePosition(int x, int y, int z)
        {
            if (_previewCube == null) return;

            var cubeGeometry = new MeshGeometry3D();
            double halfSize = 0.5;
            double centerX = x + halfSize;
            double centerY = y + halfSize;
            double centerZ = z + halfSize;

            var points = new Point3DCollection
            {
                new Point3D(centerX - halfSize, centerY - halfSize, centerZ - halfSize),
                new Point3D(centerX + halfSize, centerY - halfSize, centerZ - halfSize),
                new Point3D(centerX + halfSize, centerY + halfSize, centerZ - halfSize),
                new Point3D(centerX - halfSize, centerY + halfSize, centerZ - halfSize),
                new Point3D(centerX - halfSize, centerY - halfSize, centerZ + halfSize),
                new Point3D(centerX + halfSize, centerY - halfSize, centerZ + halfSize),
                new Point3D(centerX + halfSize, centerY + halfSize, centerZ + halfSize),
                new Point3D(centerX - halfSize, centerY + halfSize, centerZ + halfSize)
            };

            var indices = new Int32Collection
            {
                0,1,2, 0,2,3,
                4,6,5, 4,7,6,
                0,3,7, 0,7,4,
                1,5,6, 1,6,2,
                3,2,6, 3,6,7,
                0,4,5, 0,5,1
            };

            cubeGeometry.Positions = points;
            cubeGeometry.TriangleIndices = indices;

            _previewCube.Geometry = cubeGeometry;
            _previewWireframe.Transform = new TranslateTransform3D(x + 0.5, y + 0.5, z + 0.5);
        }

        private Ray3D GetRayFromMouse(Point mousePos)
        {
            var camera = MainViewport.Camera as PerspectiveCamera;
            if (camera == null) return null;

            double viewportWidth = MainViewport.ActualWidth;
            double viewportHeight = MainViewport.ActualHeight;
            if (viewportWidth <= 0 || viewportHeight <= 0) return null;

            // Normalize mouse coordinates to [-1, 1]
            double normalizedX = (2.0 * mousePos.X / viewportWidth) - 1.0;
            double normalizedY = 1.0 - (2.0 * mousePos.Y / viewportHeight);

            // Camera properties
            Point3D cameraPosition = camera.Position;
            Vector3D lookDirection = camera.LookDirection;
            Vector3D upDirection = camera.UpDirection;
            double fieldOfView = camera.FieldOfView * Math.PI / 180.0;

            // Compute view matrix axes
            Vector3D zAxis = -lookDirection;
            zAxis.Normalize();
            Vector3D xAxis = Vector3D.CrossProduct(upDirection, zAxis);
            xAxis.Normalize();
            Vector3D yAxis = Vector3D.CrossProduct(zAxis, xAxis);
            yAxis.Normalize();

            // Compute projection parameters
            double aspectRatio = viewportWidth / viewportHeight;
            double tanHalfFov = Math.Tan(fieldOfView / 2.0);
            double nearPlane = camera.NearPlaneDistance > 0 ? camera.NearPlaneDistance : 0.1;

            // Compute ray direction in view space
            double viewX = normalizedX * tanHalfFov * aspectRatio;
            double viewY = normalizedY * tanHalfFov;
            double viewZ = -1.0;

            // Transform to world space
            Vector3D rayDirection = (xAxis * viewX) + (yAxis * viewY) + (zAxis * viewZ);
            rayDirection.Normalize();

            return new Ray3D(cameraPosition, rayDirection);
        }

        private (int x, int y, int z) SnapToGrid(Point3D point)
        {
            int x = Clamp((int)Math.Round(point.X), 0, 3);
            int z = Clamp((int)Math.Round(point.Z), 0, 3);
            int y = 0; // Default to ground

            // Check if there's a cube directly beneath to stack on top
            var cubesAtXZ = _userCubes.Concat(_targetCubes).Where(p => (int)p.X == x && (int)p.Z == z);
            if (cubesAtXZ.Any())
            {
                y = (int)cubesAtXZ.Max(p => p.Y) + 1;
                // Only return this position if it's valid (has support below)
                if (IsValidPlacement(x, y, z))
                {
                    return (x, y, z);
                }
            }

            // Return ground level as fallback, but only if valid
            if (IsValidPlacement(x, 0, z))
            {
                return (x, 0, z);
            }

            // If no valid placement, return a default position (0,0,0) which will be checked later
            return (0, 0, 0);
        }

        public static int Clamp(int value, int min, int max)
        {
            return (value < min) ? min : (value > max) ? max : value;
        }

        private void AddDebugMarker(Point3D point)
        {
            // Limit to 10 markers to avoid clutter
            if (_debugMarkers.Count >= 10)
            {
                var oldMarker = _debugMarkers[0];
                MainModelGroup.Children.Remove(oldMarker);
                _debugMarkers.RemoveAt(0);
            }

            var sphere = new MeshGeometry3D();
            double radius = 0.1;
            int segments = 8;

            for (int i = 0; i <= segments; i++)
            {
                double phi = i * Math.PI / segments;
                for (int j = 0; j <= segments; j++)
                {
                    double theta = j * 2 * Math.PI / segments;
                    double x = radius * Math.Sin(phi) * Math.Cos(theta);
                    double y = radius * Math.Cos(phi);
                    double z = radius * Math.Sin(phi) * Math.Sin(theta);
                    sphere.Positions.Add(new Point3D(x, y, z));
                }
            }

            for (int i = 0; i < segments; i++)
            {
                for (int j = 0; j < segments; j++)
                {
                    int p1 = i * (segments + 1) + j;
                    int p2 = p1 + 1;
                    int p3 = (i + 1) * (segments + 1) + j;
                    int p4 = p3 + 1;

                    sphere.TriangleIndices.Add(p1);
                    sphere.TriangleIndices.Add(p2);
                    sphere.TriangleIndices.Add(p3);

                    sphere.TriangleIndices.Add(p2);
                    sphere.TriangleIndices.Add(p4);
                    sphere.TriangleIndices.Add(p3);
                }
            }

            var marker = new GeometryModel3D(sphere, new DiffuseMaterial(Brushes.Red));
            marker.Transform = new TranslateTransform3D(point.X, point.Y, point.Z);
            MainModelGroup.Children.Add(marker);
            _debugMarkers.Add(marker);
        }

        private void CheckSolution_Click(object sender, RoutedEventArgs e)
        {
            if (_userCubes.Count != _targetCubes.Count)
            {
                MessageBox.Show("Решение неверно. Количество кубов не совпадает.");
                return;
            }

            // Normalize both sets of cube positions
            var normalizedTargetCubes = NormalizeCubePositions(_targetCubes);
            var normalizedUserCubes = NormalizeCubePositions(_userCubes);

            // Check if the structure matches by comparing relative positions
            bool isCorrect = AreCubesStructurallyEquivalent(normalizedTargetCubes, normalizedUserCubes);

            MessageBox.Show(isCorrect ? "Решение верно!" : "Решение неверно. Попробуйте снова.");
        }
        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        private List<Point3D> NormalizeCubePositions(List<Point3D> cubes)
        {
            if (cubes == null || cubes.Count == 0)
                return new List<Point3D>();

            // Find the minimum coordinates
            int minX = (int)cubes.Min(p => p.X);
            int minY = (int)cubes.Min(p => p.Y);
            int minZ = (int)cubes.Min(p => p.Z);

            // Translate all cubes so the minimum coordinates are at (0,0,0)
            return cubes.Select(p => new Point3D(p.X - minX, p.Y - minY, p.Z - minZ)).ToList();
        }

        private bool AreCubesStructurallyEquivalent(List<Point3D> target, List<Point3D> user)
        {
            if (target.Count != user.Count)
                return false;

            // Sort by X, Y, Z for consistent comparison
            var sortedTarget = target.OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Z).ToList();
            var sortedUser = user.OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Z).ToList();

            for (int i = 0; i < sortedTarget.Count; i++)
            {
                if (sortedTarget[i].X != sortedUser[i].X ||
                    sortedTarget[i].Y != sortedUser[i].Y ||
                    sortedTarget[i].Z != sortedUser[i].Z)
                {
                    return false;
                }
            }
            return true;
        }
    }

    public class Ray3D
    {
        public Point3D Origin { get; }
        public Vector3D Direction { get; }

        public Ray3D(Point3D origin, Vector3D direction)
        {
            Origin = origin;
            Direction = direction;
        }
    }
}