using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SolSystem.Client;

/// <summary>
/// The unit UV sphere every body is drawn as, as plain CPU arrays.
/// </summary>
/// <remarks>
/// Two renderers need the same geometry: the fixed-function one that draws most of the bodies, and
/// the shader that draws the Earth. The Earth's day map, cloud deck and night lights have to land on
/// the same longitudes, so they must not each roll their own sphere; the half-turn UV offset that
/// puts longitude zero on the map's prime meridian lives here once.
/// </remarks>
internal static class SphereMesh
{
    internal static (VertexPositionNormalTexture[] Vertices, short[] Indices) Build(
        int longitudes, int latitudes)
    {
        var vertices = new VertexPositionNormalTexture[(longitudes + 1) * (latitudes + 1)];

        for (int lat = 0; lat <= latitudes; lat++)
        {
            double phi = Math.PI * (lat / (double)latitudes);
            float sinPhi = (float)Math.Sin(phi);
            float cosPhi = (float)Math.Cos(phi);

            for (int lon = 0; lon <= longitudes; lon++)
            {
                double theta = 2.0 * Math.PI * (lon / (double)longitudes);
                var normal = new Vector3(
                    sinPhi * (float)Math.Cos(theta),
                    sinPhi * (float)Math.Sin(theta),
                    cosPhi);

                // The half-turn offset puts the mesh's longitude zero on the map's prime meridian:
                // an equirectangular map has its u=0 at 180 degrees west, so without this every
                // body's texture is rotated half a turn and the Earth's continents are in the wrong
                // ocean.
                vertices[(lat * (longitudes + 1)) + lon] = new VertexPositionNormalTexture(
                    normal, normal,
                    new Vector2((lon / (float)longitudes) + 0.5f, lat / (float)latitudes));
            }
        }

        var indices = new List<short>();
        for (int lat = 0; lat < latitudes; lat++)
        {
            for (int lon = 0; lon < longitudes; lon++)
            {
                short a = (short)((lat * (longitudes + 1)) + lon);
                short b = (short)(a + 1);
                short c = (short)(a + longitudes + 1);
                short d = (short)(c + 1);

                indices.Add(a); indices.Add(c); indices.Add(b);
                indices.Add(b); indices.Add(c); indices.Add(d);
            }
        }

        if (vertices.Length > short.MaxValue)
        {
            throw new InvalidOperationException(
                $"the body sphere has {vertices.Length} vertices and a sixteen-bit index reaches "
                + $"{short.MaxValue}");
        }

        return (vertices, indices.ToArray());
    }
}
