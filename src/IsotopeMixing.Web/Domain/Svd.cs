namespace IsotopeMixing.Web.Domain;

public static class Svd
{
    public static SvdResult Decompose(double[][] matrix)
    {
        var rows = matrix.Length;
        var columns = matrix[0].Length;
        var gram = new double[columns][];
        for (var i = 0; i < columns; i++)
        {
            gram[i] = new double[columns];
            for (var j = 0; j < columns; j++)
                for (var row = 0; row < rows; row++)
                    gram[i][j] += matrix[row][i] * matrix[row][j];
        }

        double[] w;
        double[][] vectors;
        if (columns == 1)
        {
            w = new[] { Math.Sqrt(Math.Max(0, gram[0][0])) };
            vectors = new[] { new[] { 1d } };
        }
        else if (columns == 2)
        {
            var eigen = LinearAlgebra.Eigen2Symmetric(gram);
            w = new[]
            {
                Math.Sqrt(Math.Max(0, eigen.FirstEigenvalue)),
                Math.Sqrt(Math.Max(0, eigen.SecondEigenvalue))
            };
            vectors = new[] { eigen.FirstVector, eigen.SecondVector };
        }
        else
        {
            throw new NotSupportedException("The bundled case uses three endmembers; this deterministic implementation supports the two free coordinates imposed by sum-to-one.");
        }

        return new SvdResult(w, vectors, gram);
    }
}

public sealed record SvdResult(double[] W, double[][] V, double[][] Gram);
