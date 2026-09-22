namespace SayAll.Core.Audio;

public static class PcmPostprocessor
{
    public static short[] Process(short[] input, double gainDb)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.Length == 0)
        {
            return [];
        }

        var filtered = Array.ConvertAll(input, Convert.ToInt32);
        for (var index = 1; index < input.Length - 1; index++)
        {
            filtered[index] =
                (input[index - 1] + 2 * input[index] + input[index + 1]) >> 2;
        }

        var finiteGainDb = double.IsFinite(gainDb) ? gainDb : 0d;
        var safeGainDb = Math.Clamp(finiteGainDb, -24d, 24d);
        var gain = Math.Pow(10d, safeGainDb / 20d);

        return Array.ConvertAll(filtered, value =>
        {
            var scaled = (int)Math.Round(value * gain, MidpointRounding.AwayFromZero);
            return (short)Math.Clamp(scaled, short.MinValue, short.MaxValue);
        });
    }
}
