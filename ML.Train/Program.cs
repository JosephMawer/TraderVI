using Core.ML;
using Core.ML.Engine.Training.Classifiers;
using System;
using System.Collections.Generic;

Console.WriteLine("Offline Training for prediction models");

var dailyBars = new System.Collections.Generic.List<Core.ML.DailyBar>();
var labels = new Dictionary<DateTime, bool>(); 
var lookback = 30;

var options = new ClassificationTrainerOptions(dailyBars, labels, lookback);
ClassificationTrainerFactory.Train(ClassificationPattern.HeadAndShoulders, options);