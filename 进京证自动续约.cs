//本文件只是提供一些关键函数，不能直接运行，详情请看readme
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

public class StateData(string? apply_id_old, string? current_state, string? start_time, string? end_time, string? type)
{
    public string? Apply_id_old { set; get; } = apply_id_old;
    public string? Current_state { set; get; } = current_state;
    public string? Start_time { set; get; } = start_time;
    public string? End_time { set; get; } = end_time;
    //六环内/六环外
    public string? Type { set; get; } = type;
}
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
    var tmpCarInfo = result["data"]["bzclxx"].AsQueryable().FirstOrDefault(e => e["hphm"]!.ToString() == payLoad.hphm);
    if (tmpCarInfo is null)
        return null;

    var data = tmpCarInfo["ecbzxx"]!.AsJEnumerable().Any() ? tmpCarInfo["ecbzxx"]?.FirstOrDefault() : tmpCarInfo["bzxx"]?.FirstOrDefault();

    if (data == null)
    {
        payLoad.hpzl = tmpCarInfo["hpzl"]?.ToString();
        return null;
    }

    payLoad.hpzl = data["hpzl"]?.ToString();
    return new StateData(data["applyId"]?.ToString(), data["blztmc"]?.ToString(), data["yxqs"]?.ToString(), data["yxqz"]?.ToString(), data["jjzzlmc"]?.ToString());
}

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

