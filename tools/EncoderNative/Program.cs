using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

static class Program
{
    [StructLayout(LayoutKind.Sequential)] struct Coord { public short X,Y; public Coord(short x,short y){X=x;Y=y;} }
    [StructLayout(LayoutKind.Sequential)] struct SmallRect { public short Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct Info { public Coord Size,Cursor;public ushort Attributes;public SmallRect Window;public Coord Maximum; }
    [StructLayout(LayoutKind.Sequential)] struct CursorInfo { public uint Size;public int Visible; }
    [DllImport("kernel32.dll")] static extern nint GetStdHandle(int n);
    [DllImport("kernel32.dll")] static extern bool GetConsoleMode(nint h,out uint m);
    [DllImport("kernel32.dll")] static extern bool SetConsoleMode(nint h,uint m);
    [DllImport("kernel32.dll")] static extern bool GetConsoleScreenBufferInfo(nint h,out Info info);
    [DllImport("kernel32.dll")] static extern bool GetConsoleCursorInfo(nint h,out CursorInfo info);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool ReadConsoleOutputCharacter(nint h,StringBuilder b,uint n,Coord c,out uint read);
    [DllImport("kernel32.dll")] static extern bool ReadConsoleOutputAttribute(nint h,[Out] ushort[] b,uint n,Coord c,out uint read);
    internal static string[] Model(string sequence,int width=12,int height=4)
    {
        var cells=Enumerable.Range(0,height).Select(_=>Enumerable.Repeat(' ',width).ToArray()).ToArray();
        int x=0,y=0,top=0,bottom=height-1; char previous=' ';
        for(int i=0;i<sequence.Length;)
        {
            if(sequence[i]!='\x1b'){previous=sequence[i++];cells[y][x]=previous;x=Math.Min(width-1,x+1);continue;}
            int end=i+2; while(end<sequence.Length && !(sequence[end]>='@'&&sequence[end]<='~'))end++;
            string args=sequence.Substring(i+2,end-i-2); char op=sequence[end];
            int[] values=args.Length==0 ? [0] : args.Split(';').Select(int.Parse).ToArray();
            int n=values[0]==0?1:values[0];
            switch(op)
            {
                case 'H':y=n-1;x=(values.Length>1?Math.Max(1,values[1]):1)-1;break;
                case 'm': break;
                case 'r':top=n-1;bottom=values.Length>1?values[1]-1:height-1;x=y=0;break;
                case 'X':for(int c=x;c<Math.Min(width,x+n);c++)cells[y][c]=' ';break;
                case 'b':for(int k=0;k<n;k++){cells[y][x]=previous;x=Math.Min(width-1,x+1);}break;
                case 'S':for(int k=0;k<n;k++){for(int r=top;r<bottom;r++)Array.Copy(cells[r+1],cells[r],width);Array.Fill(cells[bottom],' ');}break;
                default: throw new Exception("unsupported model opcode "+op);
            }
            i=end+1;
        }
        return cells.Select(c=>new string(c)).ToArray();
    }
    static int Main(string[] args)
    {
        string report=args[0];var results=new List<string>();
        bool allPassed=true;
        var cases=new (string Name,string Sequence,string[] Expected)[]{
            ("erase-default","\x1b[1;1Habcdefghijkl\x1b[1;4H\x1b[5X",["abc     ijkl","            ","            ","            "]),
            ("repeat-ascii","\x1b[1;1Ha\x1b[8b",["aaaaaaaaa   ","            ","            ","            "]),
            ("scroll-region","\x1b[1;1HAAAAAAAAAAAA\x1b[2;1HBBBBBBBBBBBB\x1b[3;1HCCCCCCCCCCCC\x1b[4;1HDDDDDDDDDDDD\x1b[2;3r\x1b[1S",["AAAAAAAAAAAA","CCCCCCCCCCCC","            ","DDDDDDDDDDDD"])
        };
        var handle=GetStdHandle(-11);
        if(!GetConsoleMode(handle,out uint original))throw new Exception("interactive hidden console required");
        var encoding=Console.OutputEncoding;
        try
        {
            SetConsoleMode(handle,(original|4u)&~2u);
            Console.OutputEncoding=new UTF8Encoding(false);
            Console.Write("\x1b[?1049h\x1b[?7l");
            foreach(var item in cases)
            {
                if(!Model(item.Sequence).SequenceEqual(item.Expected))throw new Exception("independent model failed "+item.Name);
                Console.Write("\x1b[r\x1b[0m\x1b[2J\x1b[H"+item.Sequence);Console.Out.Flush();
                bool pass=true;
                for(short row=0;row<4;row++)
                {
                    var actual=new StringBuilder(12);if(!ReadConsoleOutputCharacter(handle,actual,12,new Coord(0,row),out uint read)||read!=12)throw new Exception("readback");
                    var readText=actual.ToString(0,(int)read);
                    pass &= readText==item.Expected[row];
                    if(readText!=item.Expected[row])results.Add($"{item.Name} row {row}: expected [{item.Expected[row]}] observed [{readText}]");
                }
                results.Add($"measured here: {item.Name}: independent model PASS; hidden Windows screen readback {(pass?"PASS":"FAIL")}");
                allPassed &= pass;
            }
            Console.Write("\x1b[r\x1b[0m\x1b[2J\x1b[1;1H\x1b[44mabcdefghijkl\x1b[1;4H\x1b[5X");Console.Out.Flush();
            var attrs=new ushort[12];
            if(!ReadConsoleOutputAttribute(handle,attrs,12,new Coord(0,0),out uint attributeCount)||attributeCount!=12)throw new Exception("attribute readback failed");
            bool sameAttributes=attrs.All(a=>a==attrs[0]);
            allPassed &= sameAttributes;
            results.Add("measured here: blue-background ECH attribute equivalence: "+sameAttributes);
            Console.Write("\x1b[?25h\x1b[3;5H");Console.Out.Flush();
            if(!GetConsoleScreenBufferInfo(handle,out var screen)||!GetConsoleCursorInfo(handle,out var cursor)||screen.Cursor.X!=4||screen.Cursor.Y!=2||cursor.Visible==0)throw new Exception("cursor readback failed");
            results.Add("measured here: native cursor absolute position and visible state readback PASS; presentation latency unmeasured");
        }
        finally{Console.Write("\x1b[r\x1b[0m\x1b[?7h\x1b[?25h\x1b[?1049l");Console.Out.Flush();Console.OutputEncoding=encoding;SetConsoleMode(handle,original);File.WriteAllLines(report,results);}
        return allPassed?0:1;
    }
}
