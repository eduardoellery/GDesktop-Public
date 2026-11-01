using System.Text.RegularExpressions;

namespace GDesktop;

// Parse a small, explicit search language. Never pass raw user syntax to MATCH/SQL.
internal sealed class SearchQuery
{
    enum Kind { Word, Phrase, And, Or, Not, Left, Right, End }
    record Token(Kind Kind, string Value);
    abstract record Node;
    record Term(string Text, bool Phrase):Node;
    record Binary(Node Left, Node Right, bool Or):Node;
    record Negation(Node Child):Node;
    readonly List<Token> tokens=[];
    int position;
    public List<string> Ctes {get;}=[];
    public Dictionary<string,object> Parameters {get;}=[];
    readonly List<string> positive=[];
    public string Result {get;}
    public string? PositiveQuery=>positive.Count==0?null:string.Join(" OR ",positive);

    public SearchQuery(string input)
    {
        for(int i=0;i<input.Length;)
        {
            char ch=input[i];
            if(char.IsWhiteSpace(ch)){i++;continue;}
            if(ch=='('||ch==')'){tokens.Add(new(ch=='('?Kind.Left:Kind.Right,ch.ToString()));i++;continue;}
            if(ch=='"')
            {
                i++;var text=new System.Text.StringBuilder();bool closed=false;
                while(i<input.Length)
                {
                    if(input[i]=='"')
                    {
                        if(i+1<input.Length&&input[i+1]=='"'){text.Append('"');i+=2;continue;}
                        i++;closed=true;break;
                    }
                    text.Append(input[i++]);
                }
                if(!closed)throw new ArgumentException("Feche as aspas da frase pesquisada.");
                if(!Regex.IsMatch(text.ToString(),@"[\p{L}\p{N}]"))throw new ArgumentException("Informe palavras dentro das aspas.");
                tokens.Add(new(Kind.Phrase,text.ToString()));continue;
            }
            int start=i;while(i<input.Length&&!char.IsWhiteSpace(input[i])&&input[i]!='('&&input[i]!=')'&&input[i]!='"')i++;
            string value=input[start..i];
            var kind=value switch {"AND"=>Kind.And,"OR"=>Kind.Or,"NOT"=>Kind.Not,_=>Kind.Word};
            if(kind!=Kind.Word){tokens.Add(new(kind,value));continue;}
            var words=Regex.Matches(value,@"[\p{L}\p{N}_]+");
            if(words.Count==0)throw new ArgumentException("Informe uma palavra ou uma frase entre aspas.");
            foreach(Match word in words)tokens.Add(new(Kind.Word,word.Value));
        }
        if(tokens.Count>80)throw new ArgumentException("A pesquisa contém termos ou operadores demais. Simplifique a expressão.");
        tokens.Add(new(Kind.End,""));
        var expression=ParseOr(0);
        if(Current.Kind!=Kind.End)throw new ArgumentException("Há um parêntese fechando sem abertura correspondente.");
        Result=Compile(expression);
        CollectPositive(expression,false);
    }
    Token Current=>tokens[position];
    Node ParseOr(int depth)
    {
        var left=ParseAnd(depth);
        while(Current.Kind==Kind.Or){position++;left=new Binary(left,ParseAnd(depth),true);}
        return left;
    }
    Node ParseAnd(int depth)
    {
        var left=ParseUnary(depth);
        while(true)
        {
            if(Current.Kind==Kind.And){position++;left=new Binary(left,ParseUnary(depth),false);}
            else if(Current.Kind is Kind.Word or Kind.Phrase or Kind.Left or Kind.Not)left=new Binary(left,ParseUnary(depth),false);
            else return left;
        }
    }
    Node ParseUnary(int depth)
    {
        if(depth>16)throw new ArgumentException("A pesquisa tem muitos níveis de parênteses ou de NOT.");
        if(Current.Kind==Kind.Not){position++;return new Negation(ParseUnary(depth+1));}
        if(Current.Kind==Kind.Left)
        {
            position++;var expression=ParseOr(depth+1);
            if(Current.Kind!=Kind.Right)throw new ArgumentException("Feche o parêntese da expressão pesquisada.");
            position++;return expression;
        }
        if(Current.Kind is Kind.Word or Kind.Phrase){var token=Current;position++;return new Term(token.Value,token.Kind==Kind.Phrase);}
        throw new ArgumentException("Falta uma palavra, frase ou grupo junto ao operador. Exemplo: pix OR boleto.");
    }
    static string Fts(Term term)=>"\""+term.Text.Replace("\"","\"\"")+"\""+(term.Phrase?"":"*");
    string Compile(Node node)
    {
        string select;
        switch(node)
        {
            case Term term:
                string parameter="$term"+Parameters.Count;Parameters[parameter]=Fts(term);
                select=$"SELECT DISTINCT document_id FROM search WHERE search MATCH {parameter}";
                if(!term.Phrase)
                {
                    string nameParameter="$name"+Parameters.Count;Parameters[nameParameter]=term.Text;
                    select+=$" UNION SELECT id AS document_id FROM documents WHERE instr(lower(name),lower({nameParameter}))>0";
                }
                break;
            case Negation not:
                string excluded=Compile(not.Child);
                select=$"SELECT id AS document_id FROM documents EXCEPT SELECT document_id FROM {excluded}";break;
            case Binary binary:
                string left=Compile(binary.Left),right=Compile(binary.Right);
                select=$"SELECT document_id FROM {left} {(binary.Or?"UNION":"INTERSECT")} SELECT document_id FROM {right}";break;
            default:throw new InvalidOperationException();
        }
        string name="query"+Ctes.Count;Ctes.Add(name+" AS ("+select+")");return name;
    }
    void CollectPositive(Node node,bool negated)
    {
        switch(node)
        {
            case Term term when !negated:positive.Add(Fts(term));break;
            case Negation not:CollectPositive(not.Child,!negated);break;
            case Binary binary:CollectPositive(binary.Left,negated);CollectPositive(binary.Right,negated);break;
        }
    }
}
