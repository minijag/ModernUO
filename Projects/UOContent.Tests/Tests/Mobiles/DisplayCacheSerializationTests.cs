using System;
using System.Collections.Generic;
using System.Reflection;
using Server;
using Server.Items;
using Server.Mobiles;
using Xunit;

namespace UOContent.Tests.Mobiles;

[Collection("Sequential UOContent Tests")]
public class DisplayCacheSerializationTests
{
    [Fact]
    public void LegacyV0Cache_ConsumesFixedWidthCount_AndResaves()
    {
        var donor = new Container(0);
        var cacheType = typeof(GenericBuyInfo).GetNestedType("DisplayCache", BindingFlags.NonPublic);
        var cache = (Container)Activator.CreateInstance(cacheType,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, new object[] { World.NewItem }, null);
        try
        {
            var writer = new BufferWriter(true);
            donor.Serialize(writer);
            writer.Write(0);
            writer.Write(new List<Mobile>());
            var reader = new BufferReader(writer.Buffer.AsSpan(0, (int)writer.Position).ToArray());
            cache.Deserialize(reader);
            Assert.Equal(writer.Position, reader.Position);

            var resaved = new BufferWriter(true);
            cache.Serialize(resaved);
            var rereader = new BufferReader(resaved.Buffer.AsSpan(0, (int)resaved.Position).ToArray());
            cache.Deserialize(rereader);
            Assert.Equal(resaved.Position, rereader.Position);
        }
        finally
        {
            donor.Delete();
            cache.Delete();
        }
    }
}
