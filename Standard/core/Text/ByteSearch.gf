using Standard::Collections;
// Two-Way search portions adapted from musl src/string/memmem.c.
// Copyright (c) 2005-2020 Rich Felker, et al.
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
// IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
// CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
// TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
// SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

namespace Standard::Text;

// Two-Way factorization/search adapted from musl src/string/memmem.c.
// See docs/BYTE_SEARCH.md for the upstream MIT license and algorithm reference.
public static class ByteSearch
{
    public static nuint IndexOf(readonly(byte)* data, nuint length,
        readonly(byte)* pattern, nuint count) nothrow
    {
        return Search(data,length,pattern,count,false);
    }

    public static nuint LastIndexOf(readonly(byte)* data, nuint length,
        readonly(byte)* pattern, nuint count) nothrow
    {
        if(count==0) return length;
        nuint found=Search(data,length,pattern,count,true);
        if(found==length) return length;
        return length-count-found;
    }

    // Match any member of a counted byte set, rather than a substring.
    public static nuint IndexOfAny(readonly(byte)* data, nuint length,
        readonly(byte)* pattern, nuint count) nothrow
        => SearchAny(data, length, pattern, count, false, false);

    public static nuint LastIndexOfAny(readonly(byte)* data, nuint length,
        readonly(byte)* pattern, nuint count) nothrow
        => SearchAny(data, length, pattern, count, true, false);

    /// Finds the first byte outside the counted set; an empty set selects zero without pointer reads.
    public static nuint IndexOfAnyExcept(readonly(byte)* data, nuint length,
        readonly(byte)* pattern, nuint count) nothrow
        => SearchAny(data, length, pattern, count, false, true);

    /// Finds the last byte outside the counted set; empty data always returns Length.
    public static nuint LastIndexOfAnyExcept(readonly(byte)* data, nuint length,
        readonly(byte)* pattern, nuint count) nothrow
        => SearchAny(data, length, pattern, count, true, true);

    private static nuint SearchAny(readonly(byte)* data, nuint length,
        readonly(byte)* pattern, nuint count, bool reverse, bool invert) nothrow
    {
        if (length == 0) return length;
        if (count == 0)
        {
            if (!invert) return length;
            if (reverse) return length - 1;
            return 0;
        }
        ulong[4] membership = [0ul, 0ul, 0ul, 0ul];
        for (nuint i = 0; i < count; i++)
        {
            uint value = (uint)pattern[i];
            nuint word = (nuint)(value / 64u);
            membership[word] = membership[word] | (1ul << (int)(value % 64u));
        }
        if (reverse)
        {
            nuint end = length;
            while (end != 0)
            {
                end--;
                uint value = (uint)data[end];
                bool member = (membership[(nuint)(value / 64u)] & (1ul << (int)(value % 64u))) != 0ul;
                if (member != invert)
                    return end;
            }
        }
        else
        {
            for (nuint i = 0; i < length; i++)
            {
                uint value = (uint)data[i];
                bool member = (membership[(nuint)(value / 64u)] & (1ul << (int)(value % 64u))) != 0ul;
                if (member != invert)
                    return i;
            }
        }
        return length;
    }

    // Reverse views let both public searches share the same proven invariants.
    private static byte At(readonly(byte)* data,nuint length,nuint index,bool reverse) nothrow
    {
        if(reverse) return data[length-1-index];
        return data[index];
    }

    // Returns the maximal-suffix split as a prefix length, avoiding a -1 index.
    // Opposite byte orders supply the two candidates for a critical factorization.
    private static nuint Factor(readonly(byte)* pattern,nuint count,bool reverse,
        bool opposite,nuint* period) nothrow
    {
        nuint split=0;
        nuint candidate=0;
        nuint offset=1;
        nuint step=1;
        while(offset<count-candidate)
        {
            byte left=At(pattern,count,split+offset-1,reverse);
            byte right=At(pattern,count,candidate+offset,reverse);
            if(left==right)
            {
                if(offset==step) {candidate+=step;offset=1;}
                else offset++;
            }
            else if((!opposite && left>right)||(opposite && left<right))
            {
                candidate+=offset;
                offset=1;
                step=candidate+1-split;
            }
            else
            {
                split=candidate+1;
                candidate++;
                offset=1;
                step=1;
            }
        }
        *period=step;
        return split;
    }

    private static nuint Search(readonly(byte)* data,nuint length,
        readonly(byte)* pattern,nuint count,bool reverse) nothrow
    {
        if(count==0) return 0;
        if(count>length) return length;
        nuint period=0;
        nuint otherPeriod=0;
        nuint split=Factor(pattern,count,reverse,false,&period);
        nuint other=Factor(pattern,count,reverse,true,&otherPeriod);
        if(other>split) {split=other;period=otherPeriod;}

        // A periodic left half permits retaining already matched bytes after a
        // period-sized shift. Otherwise use a safe shift longer than either half.
        bool periodic=true;
        for(nuint i=0;i<split;i++)
        {
            if(At(pattern,count,i,reverse)!=At(pattern,count,i+period,reverse))
            {periodic=false;break;}
        }
        nuint remembered=0;
        if(periodic) remembered=count-period;
        else
        {
            period=count-split+1;
            if(split>period) period=split;
        }

        nuint position=0;
        nuint memory=0;
        nuint last=length-count;
        while(true)
        {
            // Check the right half first; a mismatch determines the safe shift.
            nuint offset=split;
            if(memory>offset) offset=memory;
            while(offset<count && At(pattern,count,offset,reverse)==At(data,length,position+offset,reverse)) offset++;
            nuint shift=period;
            if(offset<count)
            {
                shift=offset-split+1;
                memory=0;
            }
            else
            {
                // The right half matched. Check the remaining left half backwards.
                offset=split;
                while(offset>memory && At(pattern,count,offset-1,reverse)==At(data,length,position+offset-1,reverse)) offset--;
                if(offset<=memory) return position;
                memory=remembered;
            }
            // Subtract before adding to avoid wrapping at pointer-size limits.
            if(shift>last-position) return length;
            position+=shift;
        }
    }
}
