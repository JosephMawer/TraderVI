1. Training phase vs. runtime phase
Training phase (offline / batch)

You:
	>> ML.Train
	
	Load historical data.

	Build feature rows or pattern windows.

	>> Core.ML.Engine.Training.Classification

	Build a pipeline (IEstimator<ITransformer>).

	Call .Fit(...) → this trains the model and returns an ITransformer.

	Evaluate metrics.

	Save the trained model to disk (e.g., model.zip).

	This is what most of the code so far is doing.




Runtime phase (online / “real time”)

Later (maybe in the same app, maybe in a different one), you:

Load the saved model from disk.

Build the features for current data (e.g., last 30 days).

Call Predict(...) on that new sample.

Use the prediction (probability, score, etc.) in your app.

The “real time” part just means:

“Whenever new data shows up, I create the right input object and call Predict.”