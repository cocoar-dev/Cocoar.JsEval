using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MimeKit;

namespace Cocoar.JsEval.Module.Smtp;

public class MMailMessage
{
    internal MimeMessage message;
    private BodyBuilder? _bodyBuilder;

    public MMailMessage()
    {
        message = new MimeMessage();
    }

    public MMailMessage From(string address)
    {
        return From(address, address);
    }

    public MMailMessage From(string name, string address)
    {
        message.From.Clear();
        message.From.Add(new MailboxAddress(Encoding.UTF8, name, address));
        return this;
    }

    public MMailMessage SetTo(string address)
    {
        message.To.Clear();
        return AddTo(address);
    }

    public MMailMessage SetTo(params string[] addresses)
    {
        message.To.Clear();
        return AddTo(addresses);
    }

    public MMailMessage SetTo(string name, string address)
    {
        message.To.Clear();
        return AddTo(name, address);
    }

    public MMailMessage AddTo(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return this;

        List<string> addrArray;
        if (address.Contains(';'))
        {
            addrArray = [.. address.Split(';')];
        }
        else
        {
            addrArray = [address.Trim()];
        }

        return AddTo([.. addrArray]);
    }

    public MMailMessage AddTo(params string[] addresses)
    {
        foreach (var address in addresses.Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            AddTo(address, address);
        }
        return this;
    }

    public MMailMessage AddTo(string name, string address)
    {
        message.To.Add(new MailboxAddress(Encoding.UTF8, name, address));
        return this;
    }

    public MMailMessage SetCc(string address)
    {
        message.Cc.Clear();
        return AddCc(address);
    }

    public MMailMessage SetCc(params string[] addresses)
    {
        message.Cc.Clear();
        return AddCc(addresses);
    }

    public MMailMessage SetCc(string name, string address)
    {
        message.Cc.Clear();
        return AddCc(name, address);
    }

    public MMailMessage AddCc(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return this;

        List<string> addrArray;
        if (address.Contains(';'))
        {
            addrArray = [.. address.Split(';')];
        }
        else
        {
            addrArray = [address.Trim()];
        }

        return AddCc([.. addrArray]);
    }

    public MMailMessage AddCc(params string[] addresses)
    {
        foreach (var address in addresses.Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            AddCc(address, address);
        }
        return this;
    }

    public MMailMessage AddCc(string name, string address)
    {
        message.Cc.Add(new MailboxAddress(Encoding.UTF8, name, address));
        return this;
    }

    public MMailMessage SetBcc(string address)
    {
        message.Bcc.Clear();
        return AddBcc(address);
    }

    public MMailMessage SetBcc(params string[] addresses)
    {
        message.Bcc.Clear();
        return AddBcc(addresses);
    }

    public MMailMessage SetBcc(string name, string address)
    {
        message.Bcc.Clear();
        return AddBcc(name, address);
    }

    public MMailMessage AddBcc(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return this;

        List<string> addrArray;
        if (address.Contains(';'))
        {
            addrArray = [.. address.Split(';')];
        }
        else
        {
            addrArray = [address.Trim()];
        }

        return AddBcc([.. addrArray]);
    }

    public MMailMessage AddBcc(params string[] addresses)
    {
        foreach (var address in addresses.Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            AddBcc(address, address);
        }
        return this;
    }

    public MMailMessage AddBcc(string name, string address)
    {
        message.Bcc.Add(new MailboxAddress(Encoding.UTF8, name, address));
        return this;
    }

    public MMailMessage WithSubject(string subject)
    {
        message.Subject = subject;
        return this;
    }

    public MMailMessage WithTextBody(string body)
    {
        _bodyBuilder ??= new BodyBuilder();

        _bodyBuilder.TextBody = body;
        return this;
    }

    public MMailMessage WithHtmlBody(string body)
    {
        _bodyBuilder ??= new BodyBuilder();

        _bodyBuilder.HtmlBody = body;
        return this;
    }

    public MMailMessage WithBody(string body, bool isHtml)
    {
        if (isHtml)
            return WithHtmlBody(body);

        return WithTextBody(body);
    }

    public static implicit operator MimeMessage(MMailMessage mMailMessage)
    {
        if (mMailMessage._bodyBuilder is not null)
            mMailMessage.message.Body = mMailMessage._bodyBuilder.ToMessageBody();

        return mMailMessage.message;
    }
}
