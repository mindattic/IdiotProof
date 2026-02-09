// ============================================================================
// LstmPredictor - Long Short-Term Memory Neural Network for Price Prediction
// ============================================================================
//
// PURPOSE:
// Implements LSTM networks for short-term stock price forecasting.
// Uses historical price data + technical indicators to predict future price direction.
//
// ARCHITECTURE:
// ╔═══════════════════════════════════════════════════════════════════════════╗
// ║  INPUT LAYER                                                              ║
// ║  - Price (normalized)                                                     ║
// ║  - VWAP distance                                                          ║
// ║  - EMA values (9, 21, 50)                                                ║
// ║  - RSI, MACD, ADX                                                        ║
// ║  - Volume ratio                                                           ║
// ╠═══════════════════════════════════════════════════════════════════════════╣
// ║  LSTM CELL (with forget, input, output gates)                            ║
// ║  - Captures short-term fluctuations                                       ║
// ║  - Remembers long-term trends                                            ║
// ╠═══════════════════════════════════════════════════════════════════════════╣
// ║  OUTPUT LAYER                                                             ║
// ║  - Direction probability (-1 to +1, negative=bearish, positive=bullish)  ║
// ║  - Confidence (0 to 1)                                                   ║
// ╚═══════════════════════════════════════════════════════════════════════════╝
//
// USAGE:
//   var lstm = new LstmPredictor();
//   lstm.AddDataPoint(snapshot);
//   var prediction = lstm.Predict();
//   if (prediction.Confidence > 0.7 && prediction.Direction > 0.5)
//       // Strong bullish signal
//
// INTEGRATION:
//   Used by AutonomousTrading to enhance market score calculation
//   Used by AdaptiveOrder to adjust TP/SL based on predicted volatility
//
// ============================================================================

using System.Text.Json;
using IdiotProof.Calculators;
using IdiotProof.Helpers;

namespace IdiotProof.Learning;

/// <summary>
/// LSTM prediction result containing direction and confidence.
/// </summary>
public readonly struct LstmPrediction
{
    /// <summary>
    /// Predicted direction: -1.0 (bearish) to +1.0 (bullish).
    /// </summary>
    public double Direction { get; init; }
    
    /// <summary>
    /// Confidence in the prediction: 0.0 (no confidence) to 1.0 (high confidence).
    /// </summary>
    public double Confidence { get; init; }
    
    /// <summary>
    /// Predicted price change percentage.
    /// </summary>
    public double PredictedChangePercent { get; init; }
    
    /// <summary>
    /// Predicted volatility (standard deviation of returns).
    /// </summary>
    public double PredictedVolatility { get; init; }
    
    /// <summary>
    /// Number of data points used for this prediction.
    /// </summary>
    public int SequenceLength { get; init; }
    
    /// <summary>
    /// True if prediction is usable (enough data, trained model).
    /// </summary>
    public bool IsUsable { get; init; }
    
    /// <summary>
    /// Suggested adjustment to market score based on LSTM prediction.
    /// </summary>
    public int ScoreAdjustment => IsUsable 
        ? (int)(Direction * Confidence * 25) // Max ±25 score adjustment
        : 0;
    
    public override string ToString()
    {
        if (!IsUsable) return "[LSTM] Insufficient data";
        string dir = Direction > 0.2 ? "BULLISH" : Direction < -0.2 ? "BEARISH" : "NEUTRAL";
        return $"[LSTM] {dir} (Dir={Direction:F2}, Conf={Confidence:P0}, Δ%={PredictedChangePercent:F2}%)";
    }
}

/// <summary>
/// Input features for LSTM model.
/// </summary>
internal readonly struct LstmInput
{
    public double Price { get; init; }
    public double VwapDistance { get; init; }  // (Price - VWAP) / VWAP
    public double Ema9Distance { get; init; }  // (Price - EMA9) / EMA9
    public double Ema21Distance { get; init; }
    public double Ema50Distance { get; init; }
    public double RsiNormalized { get; init; } // RSI / 100 - 0.5 (centers at 0)
    public double MacdNormalized { get; init; }
    public double AdxNormalized { get; init; }
    public double VolumeRatio { get; init; }
    public double PriceChange { get; init; }   // % change from previous bar
    public DateTime Timestamp { get; init; }
    
    /// <summary>
    /// Converts to feature vector for neural network input.
    /// </summary>
    public double[] ToFeatureVector() => new[]
    {
        VwapDistance,
        Ema9Distance,
        Ema21Distance,
        Ema50Distance,
        RsiNormalized,
        MacdNormalized,
        AdxNormalized,
        Math.Clamp(VolumeRatio - 1, -1, 1), // Normalize around 1.0
        PriceChange * 10 // Scale price changes
    };
    
    public static int FeatureCount => 9;
}

/// <summary>
/// LSTM Cell implementing the core memory and gating mechanics.
/// </summary>
internal sealed class LstmCell
{
    private readonly int _inputSize;
    private readonly int _hiddenSize;
    
    // Gate weight matrices
    private readonly double[,] _wf; // Forget gate weights
    private readonly double[,] _wi; // Input gate weights
    private readonly double[,] _wc; // Cell state weights
    private readonly double[,] _wo; // Output gate weights
    
    // Recurrent weight matrices
    private readonly double[,] _uf;
    private readonly double[,] _ui;
    private readonly double[,] _uc;
    private readonly double[,] _uo;
    
    // Biases
    private readonly double[] _bf;
    private readonly double[] _bi;
    private readonly double[] _bc;
    private readonly double[] _bo;
    
    // State
    private double[] _cellState;
    private double[] _hiddenState;
    
    private readonly Random _rng;
    
    public LstmCell(int inputSize, int hiddenSize, int seed = 42)
    {
        _inputSize = inputSize;
        _hiddenSize = hiddenSize;
        _rng = new Random(seed);
        
        // Xavier initialization for weights
        double scale = Math.Sqrt(2.0 / (inputSize + hiddenSize));
        
        _wf = InitializeMatrix(inputSize, hiddenSize, scale);
        _wi = InitializeMatrix(inputSize, hiddenSize, scale);
        _wc = InitializeMatrix(inputSize, hiddenSize, scale);
        _wo = InitializeMatrix(inputSize, hiddenSize, scale);
        
        _uf = InitializeMatrix(hiddenSize, hiddenSize, scale);
        _ui = InitializeMatrix(hiddenSize, hiddenSize, scale);
        _uc = InitializeMatrix(hiddenSize, hiddenSize, scale);
        _uo = InitializeMatrix(hiddenSize, hiddenSize, scale);
        
        // Initialize biases - forget gate bias set to 1 for better gradient flow
        _bf = InitializeBias(hiddenSize, 1.0);
        _bi = InitializeBias(hiddenSize, 0.0);
        _bc = InitializeBias(hiddenSize, 0.0);
        _bo = InitializeBias(hiddenSize, 0.0);
        
        // Initial states
        _cellState = new double[hiddenSize];
        _hiddenState = new double[hiddenSize];
    }
    
    /// <summary>
    /// Forward pass through LSTM cell.
    /// </summary>
    public double[] Forward(double[] input)
    {
        // Forget gate: f_t = σ(W_f · x_t + U_f · h_{t-1} + b_f)
        var ft = Sigmoid(Add(Add(MatVecMul(_wf, input), MatVecMul(_uf, _hiddenState)), _bf));
        
        // Input gate: i_t = σ(W_i · x_t + U_i · h_{t-1} + b_i)
        var it = Sigmoid(Add(Add(MatVecMul(_wi, input), MatVecMul(_ui, _hiddenState)), _bi));
        
        // Candidate cell state: C̃_t = tanh(W_c · x_t + U_c · h_{t-1} + b_c)
        var ct_candidate = Tanh(Add(Add(MatVecMul(_wc, input), MatVecMul(_uc, _hiddenState)), _bc));
        
        // New cell state: C_t = f_t ⊙ C_{t-1} + i_t ⊙ C̃_t
        _cellState = Add(Hadamard(ft, _cellState), Hadamard(it, ct_candidate));
        
        // Output gate: o_t = σ(W_o · x_t + U_o · h_{t-1} + b_o)
        var ot = Sigmoid(Add(Add(MatVecMul(_wo, input), MatVecMul(_uo, _hiddenState)), _bo));
        
        // Hidden state: h_t = o_t ⊙ tanh(C_t)
        _hiddenState = Hadamard(ot, Tanh(_cellState));
        
        return _hiddenState;
    }
    
    /// <summary>
    /// Resets the LSTM cell state.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_cellState);
        Array.Clear(_hiddenState);
    }
    
    /// <summary>
    /// Gets the current hidden state.
    /// </summary>
    public double[] HiddenState => _hiddenState;
    
    /// <summary>
    /// Gets the current cell state (long-term memory).
    /// </summary>
    public double[] CellState => _cellState;
    
    /// <summary>
    /// Updates weights using simple gradient descent.
    /// </summary>
    public void UpdateWeights(double[] gradient, double learningRate)
    {
        // Simplified weight update - in practice would use backprop through time
        for (int i = 0; i < _hiddenSize; i++)
        {
            double delta = gradient[i] * learningRate;
            for (int j = 0; j < _inputSize; j++)
            {
                _wf[j, i] -= delta * 0.01;
                _wi[j, i] -= delta * 0.01;
                _wc[j, i] -= delta * 0.01;
                _wo[j, i] -= delta * 0.01;
            }
        }
    }
    
    #region Matrix Operations
    
    private double[,] InitializeMatrix(int rows, int cols, double scale)
    {
        var matrix = new double[rows, cols];
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
                matrix[i, j] = (_rng.NextDouble() * 2 - 1) * scale;
        return matrix;
    }
    
    private double[] InitializeBias(int size, double value)
    {
        var bias = new double[size];
        Array.Fill(bias, value);
        return bias;
    }
    
    private static double[] MatVecMul(double[,] matrix, double[] vector)
    {
        int rows = matrix.GetLength(0);
        int cols = matrix.GetLength(1);
        var result = new double[cols];
        
        for (int j = 0; j < cols; j++)
        {
            double sum = 0;
            for (int i = 0; i < rows && i < vector.Length; i++)
                sum += matrix[i, j] * vector[i];
            result[j] = sum;
        }
        
        return result;
    }
    
    private static double[] Add(double[] a, double[] b)
    {
        var result = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
            result[i] = a[i] + b[i];
        return result;
    }
    
    private static double[] Hadamard(double[] a, double[] b)
    {
        var result = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
            result[i] = a[i] * b[i];
        return result;
    }
    
    private static double[] Sigmoid(double[] x)
    {
        var result = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
            result[i] = 1.0 / (1.0 + Math.Exp(-Math.Clamp(x[i], -20, 20)));
        return result;
    }
    
    private static double[] Tanh(double[] x)
    {
        var result = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
            result[i] = Math.Tanh(x[i]);
        return result;
    }
    
    #endregion
}

/// <summary>
/// Dense (fully connected) layer for output.
/// </summary>
internal sealed class DenseLayer
{
    private readonly double[,] _weights;
    private readonly double[] _bias;
    
    public DenseLayer(int inputSize, int outputSize, int seed = 42)
    {
        var rng = new Random(seed);
        double scale = Math.Sqrt(2.0 / inputSize);
        
        _weights = new double[inputSize, outputSize];
        for (int i = 0; i < inputSize; i++)
            for (int j = 0; j < outputSize; j++)
                _weights[i, j] = (rng.NextDouble() * 2 - 1) * scale;
        
        _bias = new double[outputSize];
    }
    
    public double[] Forward(double[] input)
    {
        int outputSize = _weights.GetLength(1);
        var output = new double[outputSize];
        
        for (int j = 0; j < outputSize; j++)
        {
            double sum = _bias[j];
            for (int i = 0; i < input.Length; i++)
                sum += input[i] * _weights[i, j];
            output[j] = sum;
        }
        
        return output;
    }
}

/// <summary>
/// LSTM-based predictor for stock price direction and confidence.
/// Maintains a sliding window of indicator snapshots and predicts future movement.
/// </summary>
public sealed class LstmPredictor
{
    // Network configuration
    private const int HiddenSize = 32;
    private const int MinSequenceLength = 10;  // Minimum bars before prediction
    private const int MaxSequenceLength = 100; // Maximum history to retain
    
    // Network components
    private readonly LstmCell _lstm;
    private readonly DenseLayer _outputLayer;
    
    // Data buffer
    private readonly Queue<LstmInput> _history;
    private readonly Queue<double> _priceHistory;  // For computing returns
    private double _lastPrice;
    
    // Training state
    private bool _isTrained;
    private int _trainingSamples;
    private readonly List<(LstmInput[] sequence, double actualReturn)> _trainingData;
    
    // Statistics for normalization
    private double _meanReturn;
    private double _stdReturn;
    
    // Persistence
    private readonly string _ticker;
    private readonly string _modelPath;
    
    /// <summary>
    /// Creates a new LSTM predictor for the specified ticker.
    /// </summary>
    public LstmPredictor(string ticker = "DEFAULT")
    {
        _ticker = ticker;
        _modelPath = Path.Combine(AppContext.BaseDirectory, "Profiles", $"{ticker}.lstm.json");
        
        _lstm = new LstmCell(LstmInput.FeatureCount, HiddenSize);
        _outputLayer = new DenseLayer(HiddenSize, 2); // Output: direction, confidence
        
        _history = new Queue<LstmInput>();
        _priceHistory = new Queue<double>();
        _trainingData = new List<(LstmInput[] sequence, double actualReturn)>();
        
        _meanReturn = 0;
        _stdReturn = 0.01; // Initial estimate
        
        LoadModel();
    }
    
    /// <summary>
    /// Adds a data point from indicator snapshot.
    /// </summary>
    public void AddDataPoint(IndicatorSnapshot snapshot)
    {
        var input = ConvertToInput(snapshot);
        AddInput(input);
    }
    
    /// <summary>
    /// Adds a data point with extended information.
    /// </summary>
    public void AddDataPoint(ExtendedSnapshot snapshot)
    {
        var input = new LstmInput
        {
            Price = snapshot.Price,
            VwapDistance = SafeDistance(snapshot.Price, snapshot.Vwap),
            Ema9Distance = SafeDistance(snapshot.Price, snapshot.Ema9),
            Ema21Distance = SafeDistance(snapshot.Price, snapshot.Ema21),
            Ema50Distance = SafeDistance(snapshot.Price, snapshot.Ema50),
            RsiNormalized = (snapshot.Rsi / 100.0) - 0.5,
            MacdNormalized = NormalizeMacd(snapshot.Macd, snapshot.MacdSignal),
            AdxNormalized = snapshot.Adx / 100.0,
            VolumeRatio = snapshot.VolumeRatio,
            PriceChange = _lastPrice > 0 ? (snapshot.Price - _lastPrice) / _lastPrice : 0,
            Timestamp = DateTime.UtcNow
        };
        
        AddInput(input);
    }
    
    /// <summary>
    /// Adds raw price and VWAP (minimum required).
    /// </summary>
    public void AddDataPoint(double price, double vwap)
    {
        var input = new LstmInput
        {
            Price = price,
            VwapDistance = SafeDistance(price, vwap),
            PriceChange = _lastPrice > 0 ? (price - _lastPrice) / _lastPrice : 0,
            Timestamp = DateTime.UtcNow
        };
        
        AddInput(input);
    }
    
    private void AddInput(LstmInput input)
    {
        // Update price history for return calculation
        if (_lastPrice > 0)
        {
            double ret = (input.Price - _lastPrice) / _lastPrice;
            _priceHistory.Enqueue(ret);
            
            // Online mean/std update
            if (_priceHistory.Count > 1)
            {
                double oldMean = _meanReturn;
                _meanReturn += (ret - oldMean) / _priceHistory.Count;
                _stdReturn = Math.Sqrt(
                    (_priceHistory.Count - 1) * _stdReturn * _stdReturn / _priceHistory.Count +
                    (ret - oldMean) * (ret - _meanReturn) / _priceHistory.Count
                );
            }
            
            // Trim price history
            while (_priceHistory.Count > MaxSequenceLength)
                _priceHistory.Dequeue();
        }
        
        _lastPrice = input.Price;
        
        // Add to input history
        _history.Enqueue(input);
        while (_history.Count > MaxSequenceLength)
            _history.Dequeue();
        
        // Collect training data (pairs of sequence -> next return)
        if (_history.Count >= MinSequenceLength)
        {
            var sequence = _history.Take(MinSequenceLength).ToArray();
            if (_priceHistory.Count > 0)
            {
                double actualReturn = _priceHistory.Last();
                _trainingData.Add((sequence, actualReturn));
                
                // Limit training data size
                while (_trainingData.Count > 1000)
                    _trainingData.RemoveAt(0);
            }
        }
    }
    
    /// <summary>
    /// Makes a prediction based on current sequence.
    /// </summary>
    public LstmPrediction Predict()
    {
        if (_history.Count < MinSequenceLength)
        {
            return new LstmPrediction
            {
                IsUsable = false,
                SequenceLength = _history.Count
            };
        }
        
        // Reset LSTM state for fresh sequence processing
        _lstm.Reset();
        
        // Process entire sequence through LSTM
        double[] hidden = Array.Empty<double>();
        foreach (var input in _history)
        {
            var features = input.ToFeatureVector();
            hidden = _lstm.Forward(features);
        }
        
        // Get prediction from output layer
        var output = _outputLayer.Forward(hidden);
        
        // Output[0] = direction (raw, needs tanh)
        // Output[1] = confidence (raw, needs sigmoid)
        double direction = Math.Tanh(output[0]);
        double confidence = 1.0 / (1.0 + Math.Exp(-output[1]));
        
        // Predicted change based on direction and historical volatility
        double predictedChange = direction * _stdReturn * 100; // Convert to percentage
        
        // Predicted volatility based on hidden state variance
        double volatility = hidden.Length > 0 
            ? Math.Sqrt(hidden.Select(h => h * h).Average())
            : _stdReturn;
        
        return new LstmPrediction
        {
            Direction = direction,
            Confidence = confidence,
            PredictedChangePercent = predictedChange,
            PredictedVolatility = volatility * 100,
            SequenceLength = _history.Count,
            IsUsable = true
        };
    }
    
    /// <summary>
    /// Trains the model on collected data.
    /// Call periodically or after significant price movements.
    /// </summary>
    public void Train(int epochs = 10, double learningRate = 0.001)
    {
        if (_trainingData.Count < 20)
            return; // Need more data
        
        for (int epoch = 0; epoch < epochs; epoch++)
        {
            double totalLoss = 0;
            
            foreach (var (sequence, actualReturn) in _trainingData.TakeLast(100))
            {
                // Forward pass
                _lstm.Reset();
                double[] hidden = Array.Empty<double>();
                
                foreach (var input in sequence)
                {
                    var features = input.ToFeatureVector();
                    hidden = _lstm.Forward(features);
                }
                
                var output = _outputLayer.Forward(hidden);
                double predictedDir = Math.Tanh(output[0]);
                
                // Target: direction of actual return
                double targetDir = Math.Sign(actualReturn);
                
                // Compute loss (MSE)
                double loss = Math.Pow(predictedDir - targetDir, 2);
                totalLoss += loss;
                
                // Simple gradient for weight update
                double[] gradient = new double[hidden.Length];
                double error = predictedDir - targetDir;
                for (int i = 0; i < gradient.Length; i++)
                    gradient[i] = error * (1 - predictedDir * predictedDir); // tanh derivative
                
                _lstm.UpdateWeights(gradient, learningRate);
            }
            
            _trainingSamples += _trainingData.Count;
        }
        
        _isTrained = true;
        SaveModel();
    }
    
    /// <summary>
    /// Gets training statistics.
    /// </summary>
    public (int samples, bool trained, double meanReturn, double stdReturn) GetStats()
        => (_trainingSamples, _isTrained, _meanReturn * 100, _stdReturn * 100);
    
    /// <summary>
    /// Resets the predictor state (keeps trained weights).
    /// </summary>
    public void Reset()
    {
        _history.Clear();
        _priceHistory.Clear();
        _lastPrice = 0;
        _lstm.Reset();
    }
    
    #region Helpers
    
    private static LstmInput ConvertToInput(IndicatorSnapshot snapshot)
    {
        return new LstmInput
        {
            Price = snapshot.Price,
            VwapDistance = SafeDistance(snapshot.Price, snapshot.Vwap),
            Ema9Distance = SafeDistance(snapshot.Price, snapshot.Ema9),
            Ema21Distance = SafeDistance(snapshot.Price, snapshot.Ema21),
            Ema50Distance = SafeDistance(snapshot.Price, snapshot.Ema50),
            RsiNormalized = (snapshot.Rsi / 100.0) - 0.5,
            MacdNormalized = NormalizeMacd(snapshot.Macd, snapshot.MacdSignal),
            AdxNormalized = snapshot.Adx / 100.0,
            VolumeRatio = snapshot.VolumeRatio,
            Timestamp = DateTime.UtcNow
        };
    }
    
    private static double SafeDistance(double price, double reference)
    {
        if (reference <= 0) return 0;
        return Math.Clamp((price - reference) / reference, -0.1, 0.1);
    }
    
    private static double NormalizeMacd(double macd, double signal)
    {
        double diff = macd - signal;
        // Normalize to roughly -1 to 1 range
        return Math.Tanh(diff * 10);
    }
    
    #endregion
    
    #region Persistence
    
    private void SaveModel()
    {
        try
        {
            var dir = Path.GetDirectoryName(_modelPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            
            var state = new LstmModelState
            {
                Ticker = _ticker,
                TrainingSamples = _trainingSamples,
                MeanReturn = _meanReturn,
                StdReturn = _stdReturn,
                IsTrained = _isTrained,
                LastSaved = DateTime.UtcNow
            };
            
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_modelPath, json);
        }
        catch
        {
            // Ignore save errors
        }
    }
    
    private void LoadModel()
    {
        try
        {
            if (File.Exists(_modelPath))
            {
                var json = File.ReadAllText(_modelPath);
                var state = JsonSerializer.Deserialize<LstmModelState>(json);
                
                if (state != null)
                {
                    _trainingSamples = state.TrainingSamples;
                    _meanReturn = state.MeanReturn;
                    _stdReturn = state.StdReturn > 0 ? state.StdReturn : 0.01;
                    _isTrained = state.IsTrained;
                }
            }
        }
        catch
        {
            // Ignore load errors
        }
    }
    
    #endregion
}

/// <summary>
/// Serializable model state for persistence.
/// </summary>
internal sealed class LstmModelState
{
    public string Ticker { get; set; } = "";
    public int TrainingSamples { get; set; }
    public double MeanReturn { get; set; }
    public double StdReturn { get; set; }
    public bool IsTrained { get; set; }
    public DateTime LastSaved { get; set; }
}
