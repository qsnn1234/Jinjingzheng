有好几次忘了办理进京证，被拍了，一次罚100扣一分。六环外虽说可以无限制的申请进京证，但是越是这样越容易忘呀，扣钱罚分对我这种住在燕郊，每天通勤来北京的人来说还是次要的，重要的是进京检查站发现你没有办理进京证直接让你回去，有好几次排队检查排了半个多小时终于排到我了，结果没有办理进京证，被警察叔叔命令我重新排，这种情况让我这种牛马都不想上班，一整天的心态崩溃。/(ㄒoㄒ)/~~自己就是程序员，说什么也要搞一个工具来解决这个问题，说干就干。

**注意：本教程只是说一下思路，为了减少公共资源浪费，本文对关键的接口进行了隐藏**

### 进京证办理的规则

六环外进京证虽然不限次数了，但也不是随意办理的，主要有以下规则：

- 有未处理违章或者未缴纳罚款不能办理
- 中午 12 点后不能申请当天进京证
- 可以提前申请，最多提前 3 天
- 一个帐户可以挂多辆车，但只能为其中一辆车办理
- 已办理六环外进京证，可以续办六环内进京证，成功后六环外进京证失效，有六环内进京证也可以走六环外
- 如果申请的不是当天的进京证，可以取消，但一天内只有一次取消机会，用完就不能再取消了
- 进京证在最后一天有效期时可以办理新的进京证，也称续期

办理的速度基本是很快的，一般5分钟内能办理下来，不过也有特殊情况，有一次好几个小时我的才申请通过，应该是官方的后台问题。

## 开发流程

### 1 找到登录key

就像我们登录一个系统一样，要有账号密码，登录你的进京证App也是这个道理，所以要对《北京交警》进京报文分析，查看他是如何认证的，这里使用抓包工具，如何抓包分析，这里不详细讲解，通过分析，获取到类似于`6ead6f4c93xxx4dxxxxccb4d70184aa1`这种的数据，就是登录Key，后续只需要这个key就可以进行续约，取消续约等等所有的操作。

**注意：上面提到的key的示例我用x修改了很多地方，不是真实的key，但是长度形式与真正的key一样，大家当作参考**

### 2根据key就能获得姓名、身份证信息

通过分析，申请进京证除了上一步的key外，还需要下面的参数

```csharp
public class PayLoad(string name,string pId,string carId)
{
    //这几个不知道干啥的，抓包下来的，不改就能用
    public string? cllx { set; get; } = "01";
    public string? jjzzl { set; get; } = "02";
    public float? sqdzgdjd { set; get; } = 116.40717f;
    public float? sqdzgdwd { set; get; } = 39.90469f;
    public bool ylzsfkb { set; get; } = true;
    public bool elzsfkb { set; get; } = true;
    //姓名
    public string jsrxm { set; get; } = name;
    //身份证号
    public string jszh { set; get; } = pId;
    //车牌号 格式：津ARY1354 我瞎写的一个号码
    public string hphm { set; get; } = carId;
    //车辆唯一标识
    public string? vId { set; get; }
    public string? applyIdOld { set; get; }
    public string[]? txrxx { set; get; }
    //未知
    public string? hpzl { set; get; }
    public string jjdq { set; get; } = "010";
    public string area { set; get; } = "顺义区";

    public string? jjmd { set; get; } = "06";
    public string? jjlk { set; get; } = "00606";
    public string jjmdmc { set; get; } = "其他";
    public string jjlkmc { set; get; } = "其他道路";
    //进京日期（申请生效日期）2023-02-13
    public string? jjrq { set; get; }
    //进京地址
    public string xxdz { set; get; } = "其他";
}
```

可以看到，很多都直接使用默认的就可以，但是姓名、身份证和车牌号是必须的，我们登录《北京交警》的时候，也不需要填写这些呀，肯定有获取这些信息的接口，我继续抓包~~

经过一系列操作发现，**果然有！！！！**

```csharp
//先封装一个后台请求
static async Task<JObject?> JJZRequestAsync(string url, string auth, PayLoad payLoad)
{
    using HttpClient client = new HttpClient();
    client.DefaultRequestHeaders.Add("Authorization", auth);

    var result = await client.PostAsJsonAsync(url, payLoad);
    if (result.IsSuccessStatusCode)
    {
        var resultString = await result.Content.ReadAsStringAsync();
        return JObject.Parse(resultString);

    }
    return null;
}
//创建一个类储存姓名和身份证
public class PersonInfo
{
    public string Name { get; set; }
    public string PersonId { get; set; }
}

// 通过auth直接获取姓名和身份证号码
public static async Task<PersonInfo?> GetNameAndPerId(string auth)
{
    //stateUrl为状态查询接口，需要抓包搞下来，为了解约公共资源，这里不展示，懂得人都能搞到
    var result = await JJZRequestAsync(stateUrl, auth, null);
    if (result is null)
    {
        return null;
    }
    if (result["msg"]?.ToString() == "令牌为空，请重新登录")
    {
        return null;
    }
    var tmpCarInfo = result["data"]!["bzclxx"]!.AsQueryable();
    foreach (var info in tmpCarInfo)
    {
        var bzxx = info["bzxx"]!.AsQueryable();
        if (bzxx.Count() > 0)
        {
            var name = bzxx.FirstOrDefault()!["jsrxm"]!;
            var pId = bzxx.FirstOrDefault()!["jszh"]!;
            return new PersonInfo { Name = name.ToString(), PersonId = pId.ToString() };
        }
    }
    return null;
}
```

### 3获取目前的状态

进京证肯定有以下几个信息：是否为六环内，进京证的开始和结束时间，目前状态等信息，先创建一个类

```csharp
public class StateData(string? apply_id_old, string? current_state, string? start_time, string? end_time, string? type)
{
    public string? Apply_id_old { set; get; } = apply_id_old;
    public string? Current_state { set; get; } = current_state;
    public string? Start_time { set; get; } = start_time;
    public string? End_time { set; get; } = end_time;
    //六环内/六环外
    public string? Type { set; get; } = type;
}
```

然后根据上面获得的key，以及姓名、身份证等信息就可以新建一个`PayLoad`类，来进行查询

```csharp
////获取申请状态
public static async Task<StateData?> GetStateData(string auth, PayLoad payLoad)
{
    var result = await JJZRequestAsync(stateUrl, auth, payLoad);
    if (result is null)
    {
        return null;
    }
    if (result["msg"]?.ToString() == "令牌为空，请重新登录")
    {
        return "这个就是获取到的key有问题";
    }
    //以下代码也考虑到是否一个账户下有多辆车，还有从来没有申请过进京证的情况
    var tmpCarInfo = result["data"]["bzcxx"].AsQueryable().FirstOrDefault(e => e["hphm"]!.ToString() == payLoad.hphm);
    if (tmpCarInfo is null)
        return null;

    var data = tmpCarInfo["ecbx"]!.AsJEnumerable().Any() ? tmpCarInfo["ecbzx"]?.FirstOrDefault() : tmpCarInfo["bzxx"]?.FirstOrDefault();

    if (data == null)
    {
        payLoad.hpzl = tmpCarInfo["hpzl"]?.ToString();
        return null;
    }

    payLoad.hpzl = data["hpzl"]?.ToString();
    return new StateData(data["applyId"]?.ToString(), data["blztmc"]?.ToString(), data["yxqs"]?.ToString(), data["yxqz"]?.ToString(), data["jjzzlmc"]?.ToString());
}
```

### 4开始自动续约

自动续约的url跟之前的不一样，先封装一个函数

```csharp
//自动续签
static async Task<bool> AutoReNew(string auth, PayLoad payLoad)
{
    var result = await JJZRequestAsync(insertApplRecordUrl, auth, payLoad);
    if (result is null)
    {
        return false;
    }
    if (result["code"]!.ToString().StartsWith("2"))
    {
        return true;
    }
    return false;
}
```

下面就是续签的核心函数了

```csharp
/// <summary>
/// 续签进京证
/// </summary>
/// <param name="auth">认证id</param>
/// <param name="payLoad">信息</param>
/// <returns>无需续签、申请续签成功、申请续签错误、登录过期、服务器错误</returns>
public static async Task<string> AutomaticReneWal(string auth, PayLoad payLoad)
{
    try
    {
        //获取目前的状态
        var stateData = await GetStateData(auth, payLoad);
        if (stateData is LoginErrorStateData)
        {
            Mylogger.Log($"登录过期 {payLoad.hphm}");
            return $"登录过期，请联系管理员重新授权。\r\n车牌号码:{payLoad.hphm}";
        }
        //如果当前状态是已经办理的情况，会有三种状态，则从明天开始续签
        string msg = "无需续签";
        if (new string[] { "审核通过(生效中)", "审核中", "审核通过(待生效)" }.Contains(stateData?.Current_state))
        {
            //我看网上有每天检查以下是否需要续签的，我做一个自动在到期前一天再进行续签，这样使用起来清爽，而且节省公共资源
            DateTime givenDate = DateTime.ParseExact(stateData.End_time!,"yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (stateData?.Current_state == "审核通过(生效中)")
                    {

                        //如果截止日期是今天，那马上办理
                        if (givenDate.Date == DateTime.Today)
                        {
                            payLoad.jjrq = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");
                            if (await AutoReNew(auth, payLoad))
                            {
                                msg = "申请续签成功";
                                //这个是我自己封装的方法，会自动推送到我个人的微信，用来提示是否续签成功
                                //涉及到个人的api，不过多展示，可以根据需要自行开发
                                GetStateDataDelayAndSendMsg(auth, payLoad, 10);
                            }
                            else
                            {
                                msg = "申请续签错误";
                            }
                        }
                        //如果截止日期不是今天 ，则加上定时任务
                        else
                        {
                            var user = UserManager.FindUserByCarId(payLoad.hphm);
                            if (user is null)
                            {
                                return "服务器错误";
                            }
                            else
                            {
                                //上面说了，我这是自动在到期前一天才自动续约，所以要使用定时任务
                                //不过多展示细节，可以根据自己的任务管理系统进行开发
                                await SchedulerMethod.AddJob(payLoad.hphm, user.Name, user.PersonId, user.CarId, user.MsgId, RandowDateTime.GetRandowDateTimeByDay(givenDate), auth);
                            }
                        }
                    }
            else if (stateData?.Current_state == "审核中")
            {
                msg = "审核中";
                //上面有解释该方法
                GetStateDataDelayAndSendMsg(auth, payLoad, 10);
            }
            
        }
        //如果不是以上三种状态，说明没有办理，则从今天开始续签
        else
                {
                    payLoad.jjrq = DateTime.Today.ToString("yyyy-MM-dd");
                    if (await AutoReNew(auth, payLoad))
                    {
                        msg = "申请续签成功";
                        //上面有解释该方法
                        GetStateDataDelayAndSendMsg(auth, payLoad, 10);
                    }
                    else
                    {
                        msg = "申请续签错误";
                    }
                }
        //本来打算做个枚举，懒得搞了，这样用也挺好
        switch (msg)
                {
                    case "申请续签成功":
                        return $"申请续签成功,正在等待审核通过,车牌号:{payLoad.hphm}";
                    case "申请续签错误":
                        return $"申请续签错误,车牌号:{payLoad.hphm}";
                    case "审核中":
                        return $"审核中,请稍后,车牌号:{payLoad.hphm}";
                    default:
                        var resultData = await GetStateData(auth, payLoad);
                        string title = $"{msg}\r\n{resultData?.Current_state}-{resultData?.Type}\r\n生效时间：{resultData?.Start_time} 至 {resultData?.End_time},\r\n车牌号码：{payLoad.hphm}";
                        return title;
                }

    }
    catch (Exception)
    {
        return $"出现错误，请联系管理员查看原因,车牌号:{payLoad.hphm}";
    }
}

```

## 关于获取北京交警Key

对很多人来说，使用抓包工具很麻烦，毕竟又不是搞技术的，所以我自己封装了一套搞Key的方案，自己做了一个H5页面，用户只需要输入手机号和验证码就能获得相应的key

<img src="https://qsnnimage.oss-cn-beijing.aliyuncs.com/img/202505081009923.png" alt="image-20250508100918782" style="zoom: 25%;" />

## 关于自动续约通知

申请的成功与否一定要能即使的通知给用户，以免没有因为有未处理违章等原因没有续约成功，而用户不知道，造成损失，对于通知，我调研了几种方案

1. 通过邮箱===非常不好，谁每天盯着邮箱看
2. 通过短信===好但有限，但是发短信花钱，虽然不多，另外短信服务商发的短信会被拦截，pass掉
3. 通过微信===好，大陆人目前应该离不开微信了吧，每天应该会看几眼吧

选择第三种方案，用户仅仅关注公众号，就能获取消息，该公众号无任何垃圾内容，仅仅有进京证的通知内容，通知如下：

<img src="https://qsnnimage.oss-cn-beijing.aliyuncs.com/img/202505081016508.png" alt="image-20250508101621435" style="zoom: 25%;" />

# 总结

整个进京证自动续签的流程，从自动获取用户授权->获取key->自动获取身份信息->自动续签->向用户发送续签通知的整个流程就搞定了。该系统相对网上的脚本主要有以下几个优点：

1. 可以不用抓包就取得用户授权，便捷、安全保护了用户隐私
2. 使用定时任务自动在到期前一天续约，清爽且节约公共资源
3. 及时发送续签通知，让用户时刻了解是否成功申请

交流 wx :`MaYiStudio1688`

<img src="https://qsnnimage.oss-cn-beijing.aliyuncs.com/img/202505081026394.png" alt="image-20250508102623290" style="zoom:33%;" />
